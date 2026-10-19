# הערות אבטחה — Musical Encryption (JWT, /dev, הכנה ל-Cloud Run)

מסמך זה מתעד את שינויי האבטחה שבוצעו בפרויקט השרת (ASP.NET Core 9) ובלקוח ה-WPF,
לקראת פריסה עתידית ל-Google Cloud Run. **לא בוצעה פריסה בפועל, לא הופעל billing,
ולא שונו משאבי ענן** — כל השינויים הם קוד מקומי בלבד.

---

## 1. מטרת שינויי האבטחה

לפני השינויים, השרת זיהה "משתמשים" לפי header חופשי בשם `X-User-Id` שכל לקוח יכול
לשלוח עם כל ערך שהוא — כלומר כל קורא יכול היה להתחזות לכל משתמש אחר ולגשת לרשומות
ההצפנה שלו, פשוט על ידי שינוי ה-header. בנוסף, פאנל הניהול תחת `/dev` (מחיקת
משתמשים, איפוס סיסמאות, רשימת כל המשתמשים) הוגן רק על ידי בדיקת
`ASPNETCORE_ENVIRONMENT == Development` — טעות קונפיגורציה יחידה בפריסה הייתה
חושפת אותו לאינטרנט הפתוח ללא שום הזדהות.

המטרה: להחליף את מנגנון הזיהוי ב-JWT אמיתי, להבטיח שה-`/dev` panel לא יכול
להיחשף בטעות גם בתרחיש הכי גרוע (משתנה סביבה שגוי), ולסגור פערי אבטחה נלווים
(rate limiting, חשיפת שגיאות) — הכל **לפני** שהשרת ייחשף לציבור.

---

## 2. מה יושם בפועל

### שרת (Server-Musical-Encryption)
- **JWT Bearer authentication** — `POST /api/users/login` מנפיק טוקן חתום
  (HMAC-SHA256, תוקף 30 דקות), עם ה-`User.Id` הבלתי-ניתן-לשינוי ב-claim `sub`.
- **הסרת `X-User-Id` לחלוטין** — `EncryptionController` כולו מוגן ב-`[Authorize]`;
  הזהות נקראת מה-claim המאומת, לא מ-header חופשי.
- **`GET /api/users/{id}`** מוגן — רק המשתמש עצמו יכול לבקש את החשבון שלו.
- **הגנת `/dev` ברמת הקומפילציה** — כל קוד ה-`/dev`, Scalar ו-OpenAPI עטוף ב-
  `#if DEBUG`. ב-build של `Release` (מה ש-Dockerfile מפרסם ל-Cloud Run), הקוד
  **נעדר מה-DLL לגמרי** — לא משנה מה `ASPNETCORE_ENVIRONMENT` יהיה בזמן ריצה.
- **Rate limiting** על `Register`/`Login`/`ChangePassword` — 10 בקשות לדקה לכל IP
  (ניתן לשינוי דרך `appsettings.json`, סעיף `AuthRateLimit`).
- **טיפול שגיאות ב-Production** — תגובת 500 גנרית וקבועה (JSON), לא תלויה
  בברירת המחדל הסמויה של המסגרת.
- **ניהול סוד מפתח החתימה** — דרך `dotnet user-secrets` מקומית / משתנה סביבה
  `Jwt__SigningKey` בפרודקשן. **לא** קיים בשום appsettings*.json.

### לקוח (Client-Musical-Encryption-Project / WPF)
- **תוקן שבר תאימות קריטי**: `ApiClient.LoginAsync` פירש את תגובת ה-login הישנה
  כאובייקט שטוח (`{id, username, createdAt}`), אך תגובת ה-login החדשה מקוננת
  (`{user: {...}, token, expiresAtUtc}`). ללא תיקון, `result.Username` היה חוזר
  `null` אחרי login מוצלח — נראה כמו הצלחה, אבל שבור בשקט.
- הטוקן נשמר בזיכרון התהליך בלבד (`ApiClient.AccessToken`), מצורף אוטומטית כ-
  `Authorization: Bearer <token>` לכל קריאה עתידית של אותו `ApiClient`.
- `SignOut()` מנקה את הטוקן (`ClearAccessToken()`).
- נוספה `HandleIfUnauthorized(response)` — תשתית לניקוי טוקן אוטומטי בתגובת 401
  (עדיין לא מחוברת לשום קריאה אמיתית, כי הלקוח לא קורא היום ל-`/api/encryption`
  כלל — ראו סעיף 7 למגבלה).

---

## 3. הסבר טכני

### JWT — תהליך מלא
1. `Login` מאמת username/password מול `IUserRepository` (ללא שינוי בלוגיקה הקיימת).
2. בהצלחה, `JwtTokenService.IssueToken(user)` בונה טוקן עם `sub=User.Id`,
   `name=Username`, `jti` (מזהה ייחודי), `iat`, חתום ב-HMAC-SHA256 עם מפתח מ-
   configuration (`Jwt:SigningKey`).
3. בכל קריאה מוגנת, `JwtBearerHandler` מאמת: חתימה, `iss`, `aud`, `exp` (עם
   `ClockSkew` מפורש של 30 שניות), ושה-algorithm הוא **רק** HmacSha256
   (`ValidAlgorithms`) — חוסם תקיפות "alg: none" ו-algorithm-confusion.
4. ב-Controller, `User.FindFirstValue("sub")` (עם `MapInboundClaims = false`,
   כדי לשמור את השם הקצר "sub" כפי שהונפק) הוא ה-`CallerId` היחיד שנסמכים עליו.

### תיקון תקלת configuration שעלתה תוך כדי העבודה
הגרסה הראשונה קראה את `Jwt:SigningKey` למשתנה מקומי **לפני** `builder.Build()`.
זה עבד בהרצה רגילה, אבל נכשל תחת מארח הבדיקות (`WebApplicationFactory`), שמוסיף
configuration רק בזמן קריאת ה-`Build()` עצמה. הפתרון: `JwtBearerOptions`
מוגדר כעת דרך `IOptions<JwtOptions>` שנפתר באופן עצל, בזמן הבקשה הראשונה —
אחרי שה-container כבר נבנה במלואו. אותה בעיה בדיוק תוקנה גם ב-rate limiter
(`AuthRateLimitOptions` נפתר גם הוא per-request, לא נלכד מראש).

### `/dev` — הגנה כפולה (defense in depth)
1. **זמן קומפילציה**: `#if DEBUG` — ב-build של Release, השורות האלה פשוט
   לא קיימות ב-IL. זו ההגנה האמיתית מול Cloud Run.
2. **זמן ריצה** (נשמר כשכבה נוספת ל-Debug builds): `app.Environment.IsDevelopment()`.

### Rate Limiting
`AddRateLimiter` עם policy בשם `"auth"`, `FixedWindowLimiter` **מחולק לפי IP**
(`RateLimitPartition.GetFixedWindowLimiter` עם `partitionKey` = כתובת ה-IP של
הקורא) — כך שתוקף בודד לא "ינעל" גם משתמשים לגיטימיים אחרים משותף IP-pool.
מוגדר כברירת מחדל ל-10 בקשות/דקה, ניתן לשינוי ב-`appsettings.json`.

---

## 4. מושגים מרכזיים והגדרות

- **Claim** — זוג מפתח-ערך בתוך הטוקן (למשל `sub`, `name`). נחתם קריפטוגרפית
  יחד עם שאר הטוקן, כך שלקוח לא יכול לזייף אותו מבלי לדעת את מפתח החתימה.
- **`sub` (Subject)** — ה-claim הסטנדרטי (RFC 7519) שמזהה "מי זה". כאן זה
  תמיד `User.Id`, לא ה-username — כדי שזהות לא תשתנה אם המשתמש משנה שם.
- **HMAC-SHA256** — אלגוריתם חתימה סימטרי: אותו מפתח סודי חותם ומאמת. מספיק
  כשיש שרת יחיד שגם מנפיק וגם מאמת טוקנים (לא צריך RSA/זוג מפתחות).
- **Application Default Credentials (ADC)** — מנגנון הגוגל-קלאוד שמאפשר לקוד
  לאמת מול Firestore בלי קובץ מפתח בקוד: ב-Cloud Run זה ה-service account
  המצורף לשירות; מקומית זה `gcloud auth application-default login`.
- **Fixed Window Rate Limiter** — סופר בקשות בחלון זמן קבוע (לדוגמה, כל דקה
  מתאפס המונה) ודוחה בקשות מעבר למגבלה. פשוט אך עלול לאפשר "פיצוץ" בגבול
  החלון (ראו סעיף 6 לחלופות).

---

## 5. החלטות עיצוב חשובות

| החלטה | למה |
|---|---|
| `sub` = User.Id, לא Username | Username עלול להשתנות; Id הוא קבוע ונבחר כבר ב-`User.cs` |
| HMAC-SHA256 בלבד (לא RSA) | שרת יחיד מנפיק ומאמת — אין צורך בזוג מפתחות א-סימטרי |
| תוקף טוקן: 30 דקות, ללא Refresh Token | מתאים לפרויקט לימודי; UX מקביל ל"session timeout" פשוט |
| `change-password` נשאר אנונימי | כבר דורש הוכחת OldPassword נכון בכל קריאה — שקול להזדהות עצמית; הוספת `[Authorize]` הייתה שוברת תרחיש "שכחתי טוקן אבל זוכר סיסמה" ללא תועלת אבטחתית |
| `#if DEBUG` ולא רק runtime check | חוסם את התרחיש הגרוע ביותר: `ASPNETCORE_ENVIRONMENT=Development` שהוגדר בטעות בייצור |
| Rate limiter מחולק לפי IP, לא גלובלי | תוקף בודד לא אמור "לנעול" גם משתמשים לגיטימיים אחרים |
| פרויקט הבדיקות הוא ProjectReference נפרד, לא קובץ בתוך השרת | נמנע מכפל-קומפילציה של אותם קבצי `.cs` לתוך הרכיב הראשי (ראתה בפועל בתהליך: שגיאת build עד שהוסדר `Compile Remove`) |

---

## 6. חלופות שנשקלו בפועל מול חלופות היפותטיות

**נשקלו בפועל, בתהליך העבודה הזה:**
- **קריאת configuration ישירה ל-local variable לפני `Build()`** — נוסתה
  ראשונה, **נכשלה בפועל** בבדיקות (ה-stack trace קיבל מפתח ריק), ותוקנה
  למודל `IOptions` עצל. תועד למעלה כי זו טעות אמיתית שתוקנה, לא רק אופציה
  תיאורטית.
- **Global rate limiter (לא מחולק לפי IP)** — API הפשוט ביותר
  (`AddFixedWindowLimiter(policyName, opt)`), **נדחה** בגלל סיכון DoS עצמי
  (תוקף אחד "תופס" את כל 10 הבקשות ונועל לקוחות אחרים). הוחלף ב-`AddPolicy`
  עם partition לפי IP.
- **הזזת פרויקט הבדיקות לתיקייה אחות** — נוסה, **נכשל בפועל** (sandbox
  permission denied מחוץ לתיקיית השרת/לקוח); נפתר במקום זאת עם `Compile
  Remove` ב-csproj הראשי.

**היפותטיות (לא יושמו, רק נשקלות לעתיד — ראו סעיף 8):**
- Refresh tokens עם rotation — נדחה כרגע כמורכבות מיותרת לפרויקט לימודי.
- Sliding/Token-bucket rate limiter במקום Fixed Window — היה מונע "פיצוץ
  בגבול החלון" (N בקשות בסוף דקה + N נוספות מיד בתחילת הדקה הבאה), אך
  Fixed Window פשוט יותר ומספיק כהגנה ראשונית.
- RSA/אסימטרי לחתימת JWT — רלוונטי רק אם בעתיד שירות נפרד (לא זה שמנפיק את
  הטוקן) יצטרך לאמת טוקנים בלי לדעת את הסוד.

---

## 7. ראיות לבדיקות ומגבלותיהן

**הבחנה חשובה — שלוש רמות אימות שונות בוצעו בפועל, לא רק "זה בילד":**

1. **בדיקה סטטית (קריאת קוד)** — `grep`/קריאת קבצים, לא הרצה. כך אותרה
   תקלת התאימות בלקוח (סעיף 2).
2. **בדיקות אוטומטיות (xUnit, `TestServer` בזיכרון)** — 19 בדיקות ב-Release
   config (18 ב-Debug + 1 שמדולגת במכוון עם סיבה מתועדת — ראו למטה), ללא
   Firestore אמיתי (repositories הוחלפו ב-InMemory הקיימים בפרויקט), ללא
   רשת אמיתית. מכסות: missing/invalid/valid token, גישה לרשומה של משתמש
   אחר (403), login מחזיר טוקן + מידע משתמש, רישום פתוח, רשומה חדשה משויכת
   לקורא המאומת (לא לקלט מהלקוח), ושצורת ה-JSON של login תואמת בדיוק את מה
   שהלקוח מצפה לו.
   - הערה: תחת `dotnet test` (ברירת מחדל, Debug), בדיקה אחת מדלגת במכוון:
     `DevEndpointsProductionExclusionTests` — כי קוד ה-`/dev` עדיין מקומפל
     תחת Debug (`#if DEBUG`), כך שההוכחה האמיתית דורשת `dotnet test -c
     Release` (ראו #3 למטה).
3. **בדיקת ריצה אמיתית (תהליך Release אמיתי, בקשות HTTP אמיתיות)** —
   `dotnet bin/Release/net9.0/MusicalEncryptionServer.dll` הורץ בפועל,
   `ASPNETCORE_ENVIRONMENT` נאלץ ל-`Development` בכוונה (התרחיש הגרוע
   ביותר), ונבדק עם `curl` אמיתי: `/dev`→404, `/scalar/v1`→404,
   `/openapi/v1.json`→404, `/api/encryption` ללא טוקן→401, עם טוקן מזויף
   →401. גם rate limiting נבדק באמת: 10 בקשות register ראשונות→201, ה-11
   וה-12→429.

**מגבלות משמעותיות — NOT VERIFIED:**
- **זרימת WPF-to-server אמיתית מעולם לא הורצה.** התיקון ל-`ApiClient.cs`
  מבוסס על ניתוח קוד + בדיקת build מוצלחת + בדיקת-חוזה JSON בצד השרת
  (`LoginResponseShapeTests`) — **לא** על הרצת ה-WPF client בפועל מול שרת
  חי. אין כלי GUI automation זמין בסביבה הזו לאמת את מסך ה-Login בפועל.
- **כתיבה בשוגג ל-Firestore האמיתי**: בדיקת rate-limiting הידנית (סעיף זה
  למעלה, #3) בוצעה בטעות מול ה-repository האמיתי (לא ה-InMemory), ויצרה
  10 משתמשי בדיקה אמיתיים (`smoke1`–`smoke10`) במסד הנתונים הפרודקשן
  `musical-encryption-database`. דווח למשתמש מיידית; המשתמש בחר לנקות
  בעצמו — **לא בוצע מחיקה או שינוי נוסף על ידי.**
- התנהגות בפועל מול Firestore תחת עומס/concurrency אמיתי — לא נבדק.
- אימות end-to-end מול Secret Manager/Cloud Run — לא רלוונטי כרגע (אין
  פריסה מאושרת).

---

## 8. עבודה שנותרה

**חוסם לפני חשיפה פומבית:**
- הרצת זרימת login אמיתית מה-WPF client מול שרת חי (ידנית, על ידי המשתמש —
  לא בוצעה כאן).
- ניקוי משתמשי הבדיקה `smoke1`–`smoke10` מ-Firestore (באחריות המשתמש, לפי
  בחירתו).
- Containerization + הגדרת IAM/Secret Manager ל-Cloud Run (לא בוצע, דורש
  אישור וחיוב Google Cloud נפרדים).

**מומלץ, לא חוסם:**
- `PlaceholderMusicalCipher` עדיין לא מאובטח (XOR+Base64) — לא נגעתי בזה,
  כנדרש במפורש.
- `/dev/users/{id}/records` (כפתור "Reset") מסנן לפי `username`, בעוד
  שרשומות חדשות נשמרות עם `OwnerUserId = User.Id` — לא עודכן (מחוץ לתחום
  המשימה: "אל תשנה /dev"), אך מתועד כאן כדי שלא יפתיע.
- Refresh tokens, sliding-window rate limiter — ראו סעיף 6.
- חיבור בפועל של `ApiClient.HandleIfUnauthorized` לקריאה אמיתית של
  `/api/encryption` — התשתית קיימת, אך הלקוח עדיין לא קורא לאותם endpoints
  כלל.
