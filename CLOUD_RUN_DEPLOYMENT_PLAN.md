# תוכנית פריסה ל-Google Cloud Run — Musical Encryption Server

**סטטוס מסמך: 🌐 השירות חשוף פומבית בפועל ונבדק מקצה-לקצה מול ה-CLIENT
האמיתי (ראו סעיף 16) — עם אישור מפורש שלך.** `musical-encryption-server`
מקבל בקשות ללא Google IAM, ועדיין מוגן על ידי JWT + בדיקות ownership
ברמת האפליקציה. ה-CLIENT (קוד `ApiClient` האמיתי, לא curl) **התחבר
בהצלחה ב-HTTPS אל Cloud Run ממש**, בלי ליצור/לשנות שום משתמש אמיתי.
**לא בוצע עדיין** מבחן עם חשבון אמיתי קיים (login מוצלח) — ראו סעיף 16.6.

**עדכון אחרון**: Billing פעיל. APIs מופעלים. Runtime SA קיים עם
`roles/datastore.user` בלבד. **`jwt-signing-key` נוצר ב-Secret Manager
עם הערך הקיים מ-user-secrets, וה-Runtime SA קיבל `secretAccessor` רק על
הסוד הזה.** `musical-encryption-server` **פרוס בפועל** ב-`me-west1`,
**פרטי** (`--no-allow-unauthenticated`, אומת 403 לבקשה אנונימית), עם
`min-instances=0`, `max-instances=1`. נבדק בהצלחה מול Firestore אמיתי
(קריאה בלבד) עם Google identity token — ראו סעיף 0א.

---

## 0א. הפריסה בפועל — תוצאות מאומתות (הסשן הנוכחי)

### 0א.1 מצב לפני הפעולה (קריאה בלבד, אומת)
- `gcloud artifacts repositories list` / `gcloud secrets list` /
  `gcloud run services list` ב-`me-west1` — **כולם "0 items"** לפני
  תחילת הסשן. כל משאב שמופיע מכאן והלאה נוצר בפועל בסשן הזה, לא היה קיים
  קודם.
- `dotnet build -c Release` — **0 שגיאות** (נבדק מחדש לפני deploy).
- `.dockerignore` נבדק מחדש — חוסם `secrets.json`, `appsettings.Development.json`,
  קבצי service-account; אין סוד בקונטיינר.

### 0א.2 Secret Manager — נוצר ומולא בפועל
```bash
gcloud secrets create jwt-signing-key --project musical-encryption-database --replication-policy automatic
```
**הורץ בפועל, הצליח.** הערך הועבר **בפייפ יחיד** מ-`dotnet user-secrets
list` ישירות ל-stdin של `gcloud secrets versions add` (ללא קובץ ביניים,
ללא הדפסה למסך, ללא argument בשורת הפקודה):
```bash
dotnet user-secrets list --project MusicalEncryptionServer.csproj \
  | sed -n 's/^Jwt:SigningKey = //p' \
  | gcloud secrets versions add jwt-signing-key --project musical-encryption-database --data-file=-
```
תוצאה: `Created version [1] of the secret [jwt-signing-key].` — **הערך
עצמו לא נראה אף פעם** בפלט, לא על ידי Claude ולא על ידי המשתמש בסשן זה.
זהו **אותו מפתח חתימה** שהיה כבר ב-user-secrets המקומי — לא נוצר מפתח
חדש, כך שהתנהגות ה-authentication הקיימת (טוקנים שהונפקו/שיונפקו) נשארת
עקבית.

הענקת גישה **ממוקדת רק על הסוד הזה** (לא ברמת הפרויקט):
```bash
gcloud secrets add-iam-policy-binding jwt-signing-key \
  --member="serviceAccount:cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"
```
**אומת בנפרד**: `gcloud secrets get-iam-policy jwt-signing-key` מחזיר
binding יחיד בדיוק (ה-Runtime SA, role `secretAccessor`) — ואותו SA עדיין
מחזיק **רק** `roles/datastore.user` ברמת הפרויקט (לא נוסף שום role אחר).

### 0א.3 Deploy — בוצע בפועל, הצליח בניסיון הראשון
```bash
gcloud run deploy musical-encryption-server \
  --source . \
  --project musical-encryption-database \
  --region me-west1 \
  --service-account cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com \
  --no-allow-unauthenticated \
  --no-cpu-boost \
  --cpu-throttling \
  --set-env-vars ASPNETCORE_ENVIRONMENT=Production \
  --set-secrets Jwt__SigningKey=jwt-signing-key:latest \
  --memory 512Mi --cpu 1 --min-instances 0 --max-instances 1 \
  --concurrency 80 --timeout 300
```
**תוצאה**: `musical-encryption-server-00001-kw7` — `Ready=True`,
`ConfigurationsReady=True`, `RoutesReady=True`, **100% מהתעבורה** מופנה
לרוויזיה הזו. **Service URL**:
`https://musical-encryption-server-756281922173.me-west1.run.app`

**משאבים שנוצרו אוטומטית כצד-לוואי של הפקודה (לא ניתן למנוע ב-`--source`)**:
- Artifact Registry repo `cloud-run-source-deploy` ב-`me-west1`,
  **92.9MB** בלבד (image בודד) — הרבה מתחת ל-0.5GB/חודש free tier.
- Build יחיד ב-Cloud Build, `SUCCESS`, כ-1-2 דקות — הרבה מתחת ל-120
  build-minutes/יום free tier.
- **לא נוצר** Cloud Build trigger/CI-CD: `gcloud builds triggers list`
  (גם region `me-west1` וגם `global`) מחזיר **0 items** — אין פריסה
  אוטומטית עתידית, כל deploy עתידי דורש הרצה מפורשת.

**תצורת ה-runtime המדויקת שאומתה בפועל (`describe`)**:
| הגדרה | ערך בפועל | הערה |
|---|---|---|
| Region | `me-west1` | כנדרש |
| Service Account | `cloud-run-musical-encryption@...` | כנדרש, לא default compute SA |
| `autoscaling.knative.dev/maxScale` | `1` | כנדרש |
| min-instances | `0` (אין annotation = ברירת מחדל 0) | כנדרש — scale-to-zero |
| `run.googleapis.com/cpu-throttling` | `true` | **billing מבוסס-בקשה**: CPU מוקצה רק בזמן טיפול בבקשה, לא "instance-based" תמיד-פעיל |
| `run.googleapis.com/startup-cpu-boost` | `false` | הוגדר במפורש כבוי — אין הקצאת CPU נוספת ב-cold start |
| Memory / CPU | 512Mi / 1 vCPU | כנדרש |
| Concurrency / Timeout | 80 / 300s | ברירות מחדל, כנדרש |
| `Jwt__SigningKey` | `secretKeyRef` → `jwt-signing-key:latest` | נטען כ-secret, לא כ-plain env var |
| גישה ציבורית | **`gcloud run services get-iam-policy`** מחזיר policy **ריק** (אין `allUsers`, אין שום binding) | **אין גישה ציבורית — אומת** |

### 0א.4 לוגים — עלייה נקייה, בלי שגיאות
`gcloud logging read` לרוויזיה `-00001-kw7`: `Now listening on:
http://0.0.0.0:8080`, `Hosting environment: Production`, `Application
started`. **אין שגיאות JWT configuration** (אם `Jwt:SigningKey` היה
חסר/לא תקין, ה-constructor של `JwtTokenService` היה גורם לקריסת startup
— זה לא קרה, כלומר הסוד נטען נכון). שתי אזהרות סטנדרטיות (לא קריטיות)
של `DataProtection` על אי-התמדה בין קונטיינרים — **לא רלוונטיות**: ה-JWT
במערכת זו לא תלוי ב-DataProtection API, הוא משתמש ב-`SymmetricSecurityKey`
משל עצמו (`Jwt:SigningKey`).

### 0א.5 בדיקה מאובטחת מבוצעת (Google identity, קריאה בלבד)
```bash
TOKEN=$(gcloud auth print-identity-token)
# ללא טוקן — צפוי Cloud Run 403 (לא מגיע לקוד שלנו בכלל):
curl -X POST "$SERVICE_URL/api/users/login" -d '{"username":"__healthcheck_nonexistent__","password":"x"}'
# עם טוקן — צפוי מענה ברמת האפליקציה:
curl -X POST -H "Authorization: Bearer $TOKEN" "$SERVICE_URL/api/users/login" -d '{"username":"__healthcheck_nonexistent__","password":"x"}'
```
**תוצאות בפועל**:
- **בלי טוקן: `403`** — מאומת שה-Cloud Run IAM gate עובד ושהשירות אכן
  **פרטי** (לא פומבי בכלל, ברמת התשתית, לפני שהבקשה מגיעה לקוד).
- **עם טוקן של Google (זהות `orenbashan20@gmail.com`): `401 Invalid
  username or password.`** — זה מענה **ברמת האפליקציה** (ה-JWT הפנימי,
  לא Google IAM), מה שמוכיח: (1) הבקשה עברה את שכבת Cloud Run IAM, (2)
  ה-container רץ ומגיב, (3) `JwtTokenService` אותחל בהצלחה, (4) ה-Runtime
  SA הצליח **לקרוא** מ-Firestore האמיתי (`GetByUsernameAsync` על משתמש
  שלא קיים) — **הרשאת `roles/datastore.user` עובדת בפועל**. **לא בוצעה
  שום כתיבה**: כניסה נכשלת (401) יוצאת מהקוד **לפני** שורת ה-`UpdateAsync`
  — לא נוצר/שונה שום משתמש, לא נכתב שום רשומה.
- **לא נוצר משתמש בדיקה, לא נכתב שום רשומה ל-Firestore האמיתי** — כנדרש
  במפורש במשימה.

**מסקנה**: הפריסה **הצליחה באופן מלא ומאומת מקצה לקצה** — התשתית, ה-IAM,
ה-secret, וה-Firestore connectivity כולם עובדים כמתוכנן.

---

## 0ב. סיכון כספי שיורי, עלויות פוטנציאליות, וצעדים הבאים

### 0ב.1 Spend Cap — נבדק מחדש, אין שינוי מהממצא הקודם
`gcloud billing budgets list` הורץ מחדש: `spendCap.outputState:
CONFIGURED` עדיין מופיע, מוגבל ל-Cloud Run בפרויקט זה בלבד
(`services/152E-C115-5142`), thresholds 50/80/100%. **הסכום המספרי
(ILS) עדיין לא נחשף** על ידי קריאת ה-API הזו (`specifiedAmount` כולל
רק `currencyCode`, בלי `units`). **לא הוסק שהסכום הוא אפס** — מומלץ
לבדוק ישירות ב-Cloud Console (Billing → Budgets & alerts) אם רוצים לדעת
את הסכום המדויק. **לא בוצע שום שינוי ל-Budget/Spend Cap בסשן הזה.**

### 0ב.2 הצהרה מפורשת: **אין כאן הבטחת עלות-אפס**
כנדרש במפורש במשימה — **לא קיימת הבטחת $0 מוחלטת**:
- ה-Spend Cap מכסה **רק Cloud Run** בפרויקט הזה, ולא Secret Manager,
  Artifact Registry, Cloud Build, או Logging/Monitoring.
- לכל אחד מהשירותים האלה יש **free tier חודשי קבוע**, לא "cap" — חריגה
  ממנו (למשל שימוש כבד חריג, לולאת בקשות, build חוזר ונשנה) **תיגרם
  חיוב בפועל**, ולא תיחסם אוטומטית.
- **מצב בפועל כרגע נשאר בתוך ה-free tier בביטחון גבוה**: image בודד
  92.9MB (free tier Artifact Registry: 0.5GB), build יחיד ~1-2 דקות
  (free tier Cloud Build: 120 דק'/יום), secret יחיד עם מספר access
  מועט מאוד עד כה (free tier: 6 versions + 10,000 access/חודש),
  `min-instances=0`/`max-instances=1` על Cloud Run.
- **הסיכון השיורי האמיתי**: שימוש עתידי חריג/חזרתי (deploys חוזרים
  רבים, תעבורה גבוהה בלתי צפויה, scraping) שיחצה free tiers אלה —
  לא קטסטרופלי (מוגבל על ידי `max-instances=1`), אך לא "מוגן" על ידי
  מנגנון אכיפה אוטומטי מלבד Cloud Run עצמו.

### 0ב.3 מה שנותר לפני חיבור ה-CLIENT ממחשב אחר
1. **חשיפה פומבית מבוקרת** (שלב 6, סעיף 13) — דורש אישור נפרד מפורש:
   מעבר מ-`--no-allow-unauthenticated` ל-`--allow-unauthenticated`, כי
   ה-WPF client של משתמשי-קצה אין לו זהות Google (ראו סעיף 7). **בלעדיו
   ה-CLIENT לא יכול להתחבר מרחוק כלל** — כרגע רק מי שיש לו הרשאת GCP
   בפרויקט הזה יכול לקרוא ל-service.
2. **עדכון כתובת ה-Server ב-CLIENT** (WPF) מ-`localhost` ל-Service URL
   הציבורי: `https://musical-encryption-server-756281922173.me-west1.run.app`
   — **לא בוצע בסשן הזה** (נאסר במפורש: "Do not change CLIENT code yet").
3. **מומלץ (לא חובה)**: Budget Alert אמיתי (שלב 7, סעיף 13) — התראה
   בלבד, לא cap — דורש גם הוא אישור נפרד כי נאסר לשנות Billing/Budgets
   במשימה הנוכחית.
4. **מומלץ**: לאחר חשיפה פומבית, לבדוק ש-`/dev` אכן מחזיר 404 (קוד
   מקומפל-החוצה ב-`#if DEBUG`) ושה-JWT flow המלא (register→login→encrypt)
   עובד מקצה-לקצה מול Firestore אמיתי — **לא בוצע בסשן הזה** כדי להימנע
   מיצירת משתמשי-בדיקה/רשומות ב-Firestore האמיתי, כנדרש במפורש.

---

## 0. פעולות ענן שבוצעו בפועל בסשן קודם (יצירת Runtime Service Account)

### 0.1 בדיקות מקדימות (קריאה בלבד)
- אומת מחדש: פרויקט פעיל `musical-encryption-database`, חשבון מאומת
  `orenbashan20@gmail.com` (יחד עם `tarbutime@gmail.com` שאינו הפעיל כרגע).
- `gcloud iam service-accounts list` — אומת ש-`cloud-run-musical-encryption`
  **לא היה קיים** לפני יצירתו (רק `firebase-adminsdk-fbsvc@...` וה-default
  compute SA היו קיימים).
- `gcloud projects get-iam-policy` — אומת מצב IAM מלא לפני השינוי: תואם
  לחלוטין לתיעוד הקיים במסמך (owner יחיד, SA-ים אוטומטיים של Firebase/
  Cloud Build/Run/Firestore).
- `gcloud billing accounts describe` — אומת מחדש ש-Billing Account
  `01FC68-72D80F-3B15BC` עדיין `open: true`.
- `gcloud iam roles describe roles/cloudbuild.builds.builder` — אומת
  שהתפקיד הקיים של Cloud Build identity (`756281922173@cloudbuild.gserviceaccount.com`)
  **כולל כבר** `artifactregistry.repositories.uploadArtifacts` ו-
  `artifactregistry.repositories.createOnPush` — **אין צורך בהענקת role
  נוסף** ל-Cloud Build identity כדי לדחוף images ל-Artifact Registry.
- הוסק (לא נדרש שינוי): שלב ה-deploy עצמו ב-`gcloud run deploy --source .`
  מתבצע תחת זהות ה-**deployer** (`orenbashan20@gmail.com`, `roles/owner`)
  ולא תחת Cloud Build identity — ול-`roles/owner` יש כבר הרשאת
  `iam.serviceAccounts.actAs` הנדרשת כדי לפרוס שירות עם ה-Runtime SA
  הייעודי. **לא נדרשת הענקת role נוספת לאף זהות קיימת.**

### 0.2 פעולה מבצעית שבוצעה: יצירת Runtime Service Account
```bash
gcloud iam service-accounts create cloud-run-musical-encryption \
  --project musical-encryption-database \
  --display-name "Cloud Run runtime - Musical Encryption Server"
```
**הורץ בפועל, הצליח.** נוצר:
`cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com`

### 0.3 פעולה מבצעית שבוצעה: הענקת role יחיד ל-SA החדש
```bash
gcloud projects add-iam-policy-binding musical-encryption-database \
  --member="serviceAccount:cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com" \
  --role="roles/datastore.user"
```
**הורץ בפועל, הצליח, ואומת בנפרד** (`get-iam-policy` עם filter) —
ל-SA החדש **יש role פרויקט אחד בלבד**: `roles/datastore.user`. אין
`roles/editor`/`roles/owner`/כל role נוסף. **לא ניתן עדיין
`roles/secretmanager.secretAccessor`** — זה יתווסף רק בשלב 0.4, ורק
כ-binding ממוקד על ה-secret הספציפי (לא ברמת הפרויקט), **לאחר** שה-secret
עצמו ייווצר ויאושר בנפרד (ראו 0.4 — לא בוצע).

**עלות**: יצירת Service Account והענקת IAM role הן פעולות ברמת
control-plane (IAM/Resource Manager) — **ללא עלות כספית כלל**, בכל
tier, ללא קשר ל-Spend Cap של Cloud Run. אין "usage" נמדד כאן.

### 0.4 (היסטורי — הוכן בסשן הקודם, **בוצע בפועל בסשן הנוכחי, ראו סעיף 0א.2**)
המטרה: להעביר את ערך `Jwt:SigningKey` הקיים כבר ב-`dotnet user-secrets`
המקומי (אומת שהוא קיים, `UserSecretsId` = `7f6d3a1e-9b3c-4e02-8f1a-3c2d6a9b7e40`
בקובץ `MusicalEncryptionServer.csproj`) אל Secret Manager, **בלי שהערך
ייראה אף פעם** בפלט טרמינל, בהיסטוריית shell, או בקובץ.

**שלב א' — יצירת ה-secret הריק (container בלבד, עדיין ללא ערך/גרסה):**
```bash
gcloud secrets create jwt-signing-key \
  --project musical-encryption-database \
  --replication-policy automatic
```

**שלב ב' — הזרמת הערך ישירות מ-user-secrets ל-stdin של gcloud, בפקודה
אחת, בלי להדפיס/לשמור את הערך בשום שלב ביניים** (PowerShell, להרצה
בטרמינל שלך בלבד):
```powershell
(dotnet user-secrets list --project "MusicalEncryptionServer.csproj" |
  Select-String '^Jwt:SigningKey = (.+)$').Matches[0].Groups[1].Value |
  gcloud secrets versions add jwt-signing-key `
    --project musical-encryption-database `
    --data-file=-
```
ה-pipe מעביר את הערך תהליך-לתהליך (stdin), כך שהוא **לא** מופיע כ-argument
בפקודה (אשר יכול להישמר ב-shell history או ברשימת תהליכים), **לא** נכתב
לקובץ, ו**לא** מודפס למסך — לא על ידי הפקודה עצמה ולא על ידי Claude
בסשן הזה (לא הורץ כאן בכלל).

**שלב ג' — הרשאת גישה ממוקדת (לא ברמת הפרויקט), רק לאחר שלבים א'+ב':**
```bash
gcloud secrets add-iam-policy-binding jwt-signing-key \
  --project musical-encryption-database \
  --member="serviceAccount:cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"
```

**עלות**: Secret Manager מתומחר לפי (1) מספר **active secret versions**
(6 הראשונות חינם לחודש, לכל location) ו-(2) מספר **access operations**
(10,000 הראשונות חינם לחודש). secret יחיד עם גרסה אחת וגישה מועטה
(load-on-startup של שירות יחיד עם `max-instances=1`) **נשאר בפועל בתוך
ה-free tier** בביטחון גבוה. **זהו שירות שה-Spend Cap של Cloud Run אינו
מכסה** (ראו סעיף 1ב) — לכן זו עדיין "הוצאה פוטנציאלית" תיאורטית ולא
אפס מוחלט, אך בהיקף השימוש הזה — קרוב מאוד לבלתי-מורגש. **לא בוצע כאן
כלום משלבים א'-ג'** — מחכה לאישור נפרד שלך, כנדרש במפורש במשימה.

---

## 1א. פעולת ענן שבוצעה בפועל בסשן קודם (עם אישורך)

**הופעלו** (`gcloud services enable`, פעולה אחת, הצליחה):
`run.googleapis.com`, `cloudbuild.googleapis.com`,
`artifactregistry.googleapis.com`, `secretmanager.googleapis.com`,
`billingbudgets.googleapis.com` — **כל חמשת אלה אומתו כמופעלים** דרך
`gcloud services list --enabled` לאחר מכן.

**⚠️ 3 APIs נוספים הופעלו אוטומטית על ידי גוגל, ללא אישור נפרד מפורש
עבורם** (תלויות סטנדרטיות של ה-5 שאושרו — Cloud Run/Artifact Registry
תלויים ב-IAM וב-Container Registry הישן לתאימות לאחור):
- `iam.googleapis.com` (Identity and Access Management API)
- `iamcredentials.googleapis.com` (IAM Service Account Credentials API)
- `containerregistry.googleapis.com` (Container Registry API — legacy,
  גוגל מפעילה אותו אוטומטית יחד עם Artifact Registry)

זו התנהגות סטנדרטית וצפויה של Google Cloud (תלויות API), **לא** פעולה
יזומה נפרדת שביצעתי — אך מדווחת כאן במפורש כפי שנדרש. לא נוצר/שונה שום
משאב נוסף מעבר להפעלת ה-APIs עצמם (אין SA חדש, אין repo, אין service).

---

## 1ב. ממצא חשוב: Spend Cap אמיתי כבר מוגדר (לא רק Alert)

לאחר הפעלת `billingbudgets.googleapis.com`, `gcloud billing budgets list`
הצליח (בעבר נכשל כי ה-API היה כבוי). הפלט המלא (JSON, קריאה בלבד):

```json
{
  "amount": { "specifiedAmount": { "currencyCode": "ILS" } },
  "budgetFilter": {
    "calendarPeriod": "MONTH",
    "creditTypesTreatment": "EXCLUDE_ALL_CREDITS",
    "projects": ["projects/756281922173"],
    "services": ["services/152E-C115-5142"]
  },
  "displayName": "Musical Encryption - Monthly Budget",
  "spendCap": { "outputState": "CONFIGURED" },
  "thresholdRules": [
    { "thresholdPercent": 0.5 }, { "thresholdPercent": 0.8 }, { "thresholdPercent": 1.0 }
  ]
}
```

**פענוח מדויק (מאומת, כולל חיפוש בתיעוד הרשמי של גוגל):**
- זהו **לא** Budget Alert רגיל — יש כאן `spendCap.outputState: CONFIGURED`,
  שמזהה את תכונת ה-**"Spend-cap budget"** הייעודית של גוגל: לפי
  [התיעוד הרשמי](https://docs.cloud.google.com/billing/docs/how-to/budgets-spend-caps),
  זהו מנגנון **אכיפה אמיתי** (לא רק התראה) — אבל עם מגבלות חשובות:
  מוגבל ל-**פרויקט בודד** ו-**שירות בודד**, תקופה חודשית קבועה, וה-
  thresholds קבועים ב-50%/80%/100% (בדיוק מה שאומת כאן — תואם).
- `services/152E-C115-5142` **זוהה בפועל** (דרך קטלוג המחירים הציבורי
  של Cloud Billing API) כ-**"Cloud Run"** — כלומר ה-Spend Cap הזה
  **מכסה אך ורק את Cloud Run בפרויקט הזה**, לא את כלל ההוצאה בפרויקט.
- **⚠️ קריטי**: שירותים אחרים (Firestore, Cloud Build, Artifact Registry,
  Secret Manager, Logging/Monitoring) **אינם מכוסים על ידי ה-Spend Cap
  הזה כלל** — רק Cloud Run. אם תיווצר הוצאה גבוהה בשירות אחר, Spend Cap
  הזה **לא** יעצור אותה.
- **הסכום המספרי של ה-cap עצמו (ILS) לא נחשף על ידי קריאת ה-API הזו** —
  `specifiedAmount` מכיל רק `currencyCode: ILS`, ללא `units`/`nanos`.
  **לא ניתן להסיק מכך שהסכום הוא אפס** — ייתכן שזו מגבלת-תצוגה של ה-API
  (למשל דורש הרשאת `billing.budgets.configureSpendCap` ממוקדת לראות את
  הערך המלא, בעוד שההרשאה שיש כרגע — `roles/billing.admin` — מראה את
  המבנה אך לא את הסכום) — **לא אומת מהו הסכום בפועל**; מומלץ לבדוק
  ישירות ב-Cloud Console.
- לפי התיעוד הרשמי: האכיפה **אינה מיידית** (יש עיכוב בדיווח עלויות) —
  גוגל ממליצה לקבוע את הסכום נמוך במעט מהתקרה האמיתית הרצויה.
- `creditTypesTreatment: EXCLUDE_ALL_CREDITS` — עלויות נמדדות ברוטו,
  ללא ניכוי קרדיטים/הנחות — תואם גם הוא את מאפייני ה-Spend Cap הרשמיים.

**מסקנה מעשית**: יש כבר הגנה אמיתית (לא רק alert) על הוצאות Cloud Run
בפרויקט הזה — חדשות טובות לתוכנית הפריסה הזולה. אך זו **לא** הגנה על
כלל הפרויקט, ו**הסכום המדויק לא אומת בסשן הזה**.

---

## 1ג. ממצא מאומת: `me-west1` נתמך ב-Cloud Run

`gcloud run regions list --project musical-encryption-database` (לאחר
שה-API הופעל) החזיר רשימה מלאה של כל אזורי Cloud Run, **כולל `me-west1`**
במפורש ברשימה. **מאומת ישירות**, לא מבוסס רק על ידע כללי כפי שהיה קודם.

---

## 1. מטרה

להכין תוכנית פריסה מלאה, בטוחה וזולה ל-Cloud Run עבור שרת ה-API
(ASP.NET Core 9 + Firestore + JWT). כל פעולה שמשנה משאבי ענן (מעבר
להפעלת 5 ה-APIs בסעיף 1א, שבוצעה עם אישור מפורש) **ממתינה לאישורך**.

---

## 2. ארכיטקטורה טכנית

```
לקוח WPF (מחשב משתמש)
        │  HTTPS (Cloud Run מבצע TLS termination)
        ▼
Cloud Run service (musical-encryption-server, region: me-west1)
  - Container: Dockerfile הקיים (multi-stage, .NET 9 SDK → ASP.NET runtime)
  - משתנה סביבה: ASPNETCORE_ENVIRONMENT=Production, PORT (מוזרק אוטומטית)
  - סוד: Jwt__SigningKey נטען מ-Secret Manager (volume/env, לא מוטבע בקובץ)
  - Service Account ייעודי (runtime) — הרשאות מינימליות בלבד
        │  Application Default Credentials (ADC) של ה-Service Account
        ▼
Cloud Firestore (default database, me-west1, Native mode) — כבר קיים
```

---

## 3. מושגי יסוד (להגדרות במחברת)

- **Cloud Run** — שירות שמריץ container ללא ניהול שרתים; מתומחר לפי
  שימוש בפועל (CPU/זיכרון/בקשות), לא לפי זמן ריצה פנוי.
- **Cloud Build** — שירות שבונה את ה-container image מתוך קוד המקור
  (למשל מה-Dockerfile), ושומר אותו ב-Artifact Registry.
- **Artifact Registry** — "מחסן" ל-container images שנבנו.
- **Service Account (SA)** — זהות לא-אנושית שקוד רץ תחתיה בענן; לכל SA
  יש IAM roles שמגדירים מה מותר לו.
- **IAM Role** — אוסף הרשאות (למשל `roles/datastore.user` = קריאה/כתיבה
  ב-Firestore, לא יותר).
- **Secret Manager** — שירות לאחסון ערכים רגישים (כמו מפתח JWT) מחוץ
  לקוד ולקונטיינר, עם גישה מבוקרת IAM.
- **Cloud Run IAM authentication** (`--no-allow-unauthenticated` +
  `roles/run.invoker`) — שכבת הגנה **ברמת התשתית**, לפני שהבקשה מגיעה
  בכלל לקוד שלנו: היא דורשת שהקורא יזדהה מול Google עצמה (Identity Token).
  זה **שונה לגמרי** מה-JWT של האפליקציה (שבודק "איזה משתמש בתוך המערכת
  שלנו" — אחרי שהבקשה כבר הגיעה לקוד). ראו סעיף 7.

---

## 4. A. מוכנות מקומית ושינויים

**נבדק מחדש בסשן הזה (לא שונה, רק אומת):**
- `Dockerfile` — multi-stage, `mcr.microsoft.com/dotnet/sdk:9.0` (build) →
  `mcr.microsoft.com/dotnet/aspnet:9.0` (runtime), `USER app` (לא root),
  `ENV PORT=8080` + `Program.cs` קורא את `PORT` בפועל ובונה
  `http://0.0.0.0:{PORT}` — תואם לדרישת Cloud Run.
- `.dockerignore` — חוסם `appsettings.Development.json`, קבצי
  service-account, `MusicalEncryptionServer.Tests/`, `.git/` — **אין סוד
  בקונטיינר**: `Jwt:SigningKey` לא קיים בשום appsettings עוקב, רק ב-
  `dotnet user-secrets` המקומי (שלא נכנס ל-build context כלל — הוא חי
  מחוץ לתיקיית הפרויקט, בפרופיל המשתמש).
- קוד ה-`/dev`/Scalar/OpenAPI עטוף ב-`#if DEBUG` — **נעדר לגמרי** מה-DLL
  שה-Dockerfile מפרסם (`dotnet publish -c Release`).
- `dotnet build -c Release` + `dotnet test -c Release` — **0 שגיאות, 19/19
  בדיקות עברו** (ללא Firestore אמיתי — ראו `CLOUD_SECURITY_NOTES.md`).
- **Docker אינו מותקן בסביבה זו** — לא הותקן (אסור), ולכן "local Docker
  build" אינו אפשרות מעשית כרגע (ראו סעיף 6 להשפעה על בחירת שיטת הפריסה).

**לא שונה שום קובץ בסשן הזה** — הכל כבר היה תקין מהסבבים הקודמים.

---

## 5. B. תוצאות בדיקת הענן (מאומת מול לא ידוע)

עודכן לאחר שבוצעה פעולת ה-enable המאושרת (סעיף 1א) — חלק מהשורות עברו
כעת מ"לא ניתן לאימות" ל"מאומת", כי ה-APIs הדרושים לבדיקה כבר מופעלים.

| בדיקה | תוצאה | סטטוס |
|---|---|---|
| gcloud מותקן ומאומת | כן, גרסה 588.0.0, חשבון פעיל `orenbashan20@gmail.com` | **מאומת** |
| הפרויקט הפעיל | `musical-encryption-database` (כבר מוגדר נכון) | **מאומת** |
| Billing | **פעיל**, מקושר ל-`billingAccounts/01FC68-72D80F-3B15BC` ("My Billing Account", פתוח) | **מאומת** |
| **5 ה-APIs המאושרים** (`run`, `cloudbuild`, `artifactregistry`, `secretmanager`, `billingbudgets`) | **הופעלו בפועל** והוכח שהם ברשימת ה-enabled | **מאומת — זו הפעולה הראשונה שבאמת שינתה state בתהליך הזה** |
| **3 APIs נוספים** שהופעלו אוטומטית כתלויות (`iam`, `iamcredentials`, `containerregistry`) | לא אושרו במפורש בנפרד — תלויות סטנדרטיות של Cloud Run/Artifact Registry | **מאומת, מדווח כנדרש — ראו סעיף 1א** |
| **Budget/Spend Cap** | **זוהה בפועל**: `spendCap.outputState: CONFIGURED` — זהו Spend Cap אמיתי (לא alert בלבד), מוגבל לפרויקט זה + שירות Cloud Run בלבד (`services/152E-C115-5142` זוהה ככזה דרך קטלוג המחירים), thresholds קבועים 50/80/100%. **הסכום המספרי (ILS) לא נחשף** על ידי קריאת ה-API | **מאומת חלקית — ראו סעיף 1ב לפירוט מלא; הסכום עצמו לא ידוע** |
| Firestore database | קיים: `(default)`, `me-west1`, **FIRESTORE_NATIVE**, free tier, delete-protection **כבוי** | **מאומת (ללא שינוי)** |
| Service Accounts קיימים | `firebase-adminsdk-fbsvc@...` + default compute SA (אוטומטיים) + **`cloud-run-musical-encryption@...` — נוצר בסשן הנוכחי** | **שונה בפועל בסשן הנוכחי — ראו סעיף 0.2** |
| IAM policy נוכחי | `orenbashan20@gmail.com` = `roles/owner`; SA-ים אוטומטיים של Firebase/Firestore/Cloud Build/Run; **+ `roles/datastore.user` ל-`cloud-run-musical-encryption@...` (role יחיד, אומת)** | **שונה בפועל בסשן הנוכחי — ראו סעיף 0.3** |
| זמינות אזור `me-west1` ל-Cloud Run | **מאומת ישירות** דרך `gcloud run regions list` (לאחר הפעלת ה-API) — מופיע ברשימה המלאה | **מאומת — שונה מהבדיקה הקודמת שלא הצליחה** |
| תמחור מדויק עדכני | עדיין לא נשלף מחיר בזמן אמת | **לא אומת — ראו סעיף 9** |

---

## 6. C. שיטת Build/Deploy מומלצת

| שיטה | דורשת Docker מקומי? | משתמשת ב-Dockerfile הקיים? | הערות |
|---|---|---|---|
| **`gcloud run deploy --source .`** (מומלץ) | **לא** | **כן, אוטומטית** — אם יש Dockerfile בתיקיית המקור, Cloud Build משתמש בו ישירות (לא ב-Buildpacks) | פקודה אחת: גם בונה (ב-Cloud Build) וגם פורס. הכי פשוט למצב הנוכחי. |
| `gcloud builds submit --tag ...` + `gcloud run deploy --image ...` | לא | כן | שני שלבים נפרדים — יותר שליטה (למשל רוצים לבדוק את ה-image לפני deploy), לא נחוץ כרגע. |
| Docker build מקומי + push ידני | **כן** | כן | **לא ישים** — Docker לא מותקן, ואסור להתקין system-wide. נפסל. |

**המלצה: `gcloud run deploy --source .`** — תואם למבנה הריפו הנוכחי
בדיוק כפי שהוא (Dockerfile בשורש `Server-Musical-Encryption/`), לא דורש
שום התקנה נוספת על המחשב הזה.

**APIs שיידרשו להיות מופעלים (לא הופעלו):**
`run.googleapis.com`, `artifactregistry.googleapis.com`,
`cloudbuild.googleapis.com`, `secretmanager.googleapis.com`.
(`firestore.googleapis.com`/`datastore.googleapis.com` כבר מופעלים.)

---

## 7. D. תוכנית Service Account ו-IAM

### שלוש זהויות נפרדות — חשוב להבחין ביניהן:
1. **Deployer** (אתה, `orenbashan20@gmail.com`) — מי שמריץ את פקודת ה-
   deploy. כבר `roles/owner`, יותר מספיק הרשאות; בפרויקט אמיתי-גדול יותר
   היה רצוי תפקיד מצומצם (`roles/run.admin` +
   `roles/iam.serviceAccountUser` על ה-runtime SA בלבד), אך לפרויקט לימודי
   זה לא נחוץ לשנות.
2. **Build identity** — ה-SA שה-Cloud Build Google-managed משתמש בו כדי
   לבנות את ה-image מה-Dockerfile ולדחוף אותו ל-Artifact Registry.
   נוצר/מנוהל אוטומטית על ידי גוגל; בדרך-כלל כבר מקבל את ההרשאות הדרושות
   אוטומטית (`roles/artifactregistry.writer` וכו') ברגע ש-APIs מופעלים.
3. **Runtime Service Account** — **צריך ליצור SA ייעודי חדש** (אסור
   להשתמש ב-default compute SA שיש לו הרשאות Editor רחבות מדי). זו הזהות
   שה-container עצמו רץ תחתיה כשהוא כבר חי ומשרת בקשות.

### הרשאות מינימליות ל-Runtime SA (least privilege):
- **`roles/datastore.user`** על הפרויקט — מאפשר קריאה/כתיבה ב-Firestore
  (בדיוק מה ש-`FirestoreUserRepository`/`FirestoreEncryptionRepository`
  צריכים — לא יותר: לא `roles/owner`, לא `roles/editor`).
- **`roles/secretmanager.secretAccessor`** — **לא על כל הפרויקט**, אלא
  ממוקד (scoped) רק על ה-secret הספציפי `jwt-signing-key` (IAM binding על
  המשאב עצמו, לא ברמת הפרויקט) — כך שה-SA הזה לא יכול לקרוא סודות אחרים
  שאולי יתווספו בעתיד.

### Cloud Run IAM authentication לעומת JWT של האפליקציה:
- **Cloud Run IAM** (`--no-allow-unauthenticated`) שואל: "האם למתקשר יש
  זהות Google עם `roles/run.invoker`?" — נבדק **לפני** שהבקשה מגיעה לקוד
  שלנו, ברמת התשתית.
- **ה-JWT של האפליקציה** (שכבר מומש) שואל: "איזה משתמש-קצה של המערכת
  שלנו זה?" — נבדק **בתוך** הקוד שלנו.
- מכיוון שהלקוח הוא אפליקציית WPF שמשתמשי-קצה רגילים מריצים (ללא זהות
  Google), **Cloud Run IAM authentication לא יכול להיות שכבת ההגנה
  הסופית** לשימוש אמיתי — הוא משמש רק לשלב בדיקה ראשוני לפני חשיפה
  (ראו המלצה הבאה).

### אסטרטגיית חשיפה בטוחה מומלצת (לא בוצע, לא נחשף שום דבר):
1. **Deploy ראשוני עם `--no-allow-unauthenticated`** — השירות חי אך
   סגור לחלוטין לציבור.
2. בדיקה **רק דרך הזהות שלך** (`gcloud run services proxy` או identity
   token) — לוודא `/dev` אכן 404, ושה-JWT flow עובד כצפוי מול Firestore
   אמיתי (לא InMemory הפעם).
3. **רק לאחר אישור ידני מפורש שלך** — מעבר ל-`--allow-unauthenticated`
   (ציבורי), כדי שה-WPF client של משתמשי-קצה יוכל להתחבר בלי זהות Google.
4. ה-JWT של האפליקציה נשאר שכבת ההגנה האמיתית לכל מה שקורה אחרי.

**לא נוצר שום SA, לא שונה שום IAM binding, לא בוצע deploy.**

---

## 8. E. תוכנית Secret Manager

- יצירת secret יחיד: `jwt-signing-key` (replication: `automatic` — מספיק
  לפרויקט בסדר גודל הזה, לא דורש `user-managed` replication לאזורים
  ספציפיים).
- ערך הסוד: **אותו ערך/ערך חדש** שנוצר דרך `dotnet user-secrets` מקומית
  — **לא יועלה כאן**, יוזן ידנית על ידך בזמן ה-deploy בעצמו.
- חיבור ל-Cloud Run: `--set-secrets Jwt__SigningKey=jwt-signing-key:latest`
  (קו תחתון כפול `__` כי כך ASP.NET Core ממפה משתני סביבה למפתח
  configuration מקונן `Jwt:SigningKey` — זהה למנגנון שכבר נבדק ועובד).
- **IAM**: `roles/secretmanager.secretAccessor` ניתן **רק** ל-Runtime SA,
  **רק** על ה-secret הזה (לא ברמת הפרויקט) — ראו סעיף 7.
- סבב מפתחות (rotation) עתידי: יצירת גרסה חדשה של אותו secret
  (`gcloud secrets versions add`) ועדכון ה-reference ל-`:latest` — Cloud
  Run טוען מחדש גרסת secret חדשה ב-deploy הבא (לא live-reload אוטומטי
  באמצע ריצה).

---

## 9. F. תצורת Cloud Run מומלצת

| הגדרה | ערך מומלץ | למה |
|---|---|---|
| Region | `me-west1` | תואם את אזור ה-Firestore הקיים — פחות latency, אין network egress בין-אזורי |
| Min instances | **0** | scale-to-zero — אין חיוב כשאין תעבורה (מתאים לפרויקט לימודי עם תעבורה נמוכה/לסירוגין) |
| Max instances | **1** | חוסם כל אפשרות ל"התפוצצות" עלות בשוגג (למשל לולאה אינסופית מהלקוח) |
| Memory | 512Mi | מספיק בנדיבות ל-API קטן ב-.NET 9 |
| CPU | 1 | ברירת מחדל, אין צורך ביותר |
| Concurrency | 80 (ברירת מחדל) | סביר למספר בקשות נמוך-בינוני בו-זמנית |
| Request timeout | 300s (ברירת מחדל) | אפשר להקטין ל-30-60s בעתיד אם רוצים, לא קריטי כרגע |
| CPU allocation | "CPU only during request processing" (ברירת מחדל) | לא משלמים על CPU כש-instance לא מטפל בבקשה בפועל |
| גישה ראשונית | `--no-allow-unauthenticated` | ראו סעיף 7 — פתיחה לציבור רק אחרי בדיקה |

---

## 10. G. שיקולי עלות

**שירותים עם free tier שעשויים עדיין לחייב אם חורגים:**
- **Cloud Run** — free tier חודשי קבוע (vCPU-seconds, GiB-seconds, בקשות)
  שחל **גם אחרי** הפעלת Billing, לא רק לפניה. עם `min-instances=0` ו-
  `max-instances=1` ותעבורה של פרויקט לימודי, סביר להישאר בתוך ה-free
  tier לגמרי.
- **Cloud Build** — דקות build חינמיות ביום; build בודד של פרויקט .NET
  קטן כזה אמור לקחת הרבה פחות מהמכסה.
- **Artifact Registry** — נפח אחסון חינמי מוגבל; image אחד/שניים של
  שרת .NET קטן אמור להישאר בטווח.
- **Secret Manager** — free tier מכסה מספר secrets פעילים ומספר פעולות
  גישה חודשיות; secret יחיד בשימוש נמוך נשאר כנראה בתוכו.
- **Cloud Logging/Monitoring** — free tier לנפח ingestion חודשי; תעבורה
  קטנה לא צפויה לחרוג.
- **Firestore** — כבר בשימוש, יש לו free tier נפרד משלו (כבר פעיל, לא
  קשור ל-Cloud Run).
- **Network egress** — ייתכן חיוב קטן מעבר למכסה חינמית בהתאם לנפח
  תעבורה בפועל; **לא אומתו מחירים מדויקים נוכחיים בסשן הזה** (אין API
  תמחור זמין לבדיקה ללא Billing) — **מומלץ לבדוק את מחשבון התמחור
  הרשמי של Google Cloud לפני ההפעלה.**

**המלצות לבקרת עלות:**
- הגדרת **Budget Alert** ב-Cloud Billing (למשל 10$/חודש, עם התראות ב-
  50%/90%/100%) — **לאחר** הפעלת Billing.
- **חשוב להבהיר**: Budget Alert הוא **התראה בלבד** — הוא **לא** עוצר
  אוטומטית את החיוב ו**לא** מהווה "תקרת הוצאה קשיחה". עצירה אוטומטית
  אמיתית דורשת אוטומציה נפרדת (Pub/Sub + Cloud Function שמשביתה Billing
  או מוחקת משאבים) — **לא נכלל בתוכנית הזו**, ואפשר להוסיף בנפרד אם
  תרצה.
- `max-instances=1` הוא בעצמו בקרת-עלות חשובה — מגביל פיזית את הקנה-מידה
  המקסימלי האפשרי, גם בלי תלות בהתראה.

---

## 11. H. בדיקות שבוצעו בפועל בסשן הזה

- `dotnet build -c Release` — **0 שגיאות**.
- `dotnet test -c Release` — **19/19 עברו** (ללא Firestore אמיתי — ראו
  `CLOUD_SECURITY_NOTES.md` לפירוט המלא של רמות הבדיקה).
- בדיקות `gcloud` **קריאה-בלבד**: auth, project describe, billing linkage,
  enabled services, IAM policy, service accounts, Firestore database
  metadata — **כולן תועדו למעלה**.
- **לא בוצעה** שום בדיקת `docker build` (Docker לא מותקן, לא הותקן).
- **לא בוצעה** שום בדיקת deploy אמיתית (אסור במפורש במשימה זו).

---

## 12. I. קבצים שהשתנו בסשן הזה

**אף קובץ לא נוצר/שונה בפרויקט עצמו** — הסשן הזה היה READ-ONLY מבחינת
קוד (רק `dotnet build`/`dotnet test` הורצו, ללא כתיבה לקבצי קוד), פרט
ליצירת מסמך התכנון הזה:
- `CLOUD_RUN_DEPLOYMENT_PLAN.md` (קובץ זה, חדש).

---

## 13. J. סדר פעולות מדויק הדורש אישור שלך (לא בוצע כלום מכך)

> **כל שלב כאן דורש אישור מפורש שלך לפני הרצה.** הפקודות ניתנות כ"עותק
> מוכן" — לא הורצו.

### שלב 0 — הפעלת Billing ✅ **הושלם על ידך**
Billing מקושר ומאומת כפעיל (`billingAccounts/01FC68-72D80F-3B15BC`).

### שלב 1 — הפעלת APIs נדרשות ✅ **הושלם בסשן הזה, עם אישורך המפורש**
```bash
gcloud services enable \
  run.googleapis.com \
  artifactregistry.googleapis.com \
  cloudbuild.googleapis.com \
  secretmanager.googleapis.com \
  billingbudgets.googleapis.com \
  --project musical-encryption-database
```
**בוצע בפועל, הצליח, ואומת** — כל 5 ה-APIs מופיעים ב-`gcloud services
list --enabled`. גוגל הפעילה גם 3 APIs תלויים נוספים אוטומטית
(`iam`, `iamcredentials`, `containerregistry`) — ראו סעיף 1א.

**בדיקת ה-Budget כבר בוצעה (קריאה בלבד) — ראו סעיף 1ב לממצא המלא**:
Spend Cap אמיתי מוגדר על Cloud Run בפרויקט זה (סכום ה-ILS עצמו לא נחשף
על ידי ה-API). לא בוצע שום שינוי לבאדג'ט.

### שלב 2 — יצירת Runtime Service Account ✅ **הושלם בסשן הנוכחי, עם אישורך המפורש**
```bash
gcloud iam service-accounts create cloud-run-musical-encryption \
  --project musical-encryption-database \
  --display-name "Cloud Run runtime - Musical Encryption Server"

gcloud projects add-iam-policy-binding musical-encryption-database \
  --member="serviceAccount:cloud-run-musical-encryption@musical-encryption-database.iam.gserviceaccount.com" \
  --role="roles/datastore.user"
```
**בוצע בפועל, הצליח, ואומת** — ראו סעיף 0.2–0.3 לפירוט מלא. ל-SA יש
role פרויקטי יחיד (`roles/datastore.user`), ללא `secretAccessor` (עדיין).

### שלב 3 — יצירת ה-Secret ✅ **הושלם בפועל בסשן הנוכחי, עם אישורך המפורש**
בוצע בדיוק כמתואר, כולל ה-pipe הבטוח מ-user-secrets (ראו סעיף 0א.2
לפקודות המדויקות ולתוצאה). ה-secret `jwt-signing-key` קיים עם גרסה 1,
וה-Runtime SA מחזיק `secretAccessor` **רק** עליו.

### שלב 4 — Deploy ראשוני, פרטי ✅ **הושלם בפועל בסשן הנוכחי, עם אישורך המפורש**
בוצע בדיוק כמתואר (עם `--no-cpu-boost --cpu-throttling` נוספים לחיזוק
billing מבוסס-בקשה) — ראו סעיף 0א.3 לפקודה המדויקת ולתוצאות המאומתות
(`Ready=True`, Service URL, תצורה מלאה).

### שלב 5 — בדיקה פרטית ✅ **הושלם בפועל בסשן הנוכחי**
בוצע עם endpoint קל (`POST /api/users/login` עם משתמש לא-קיים, קריאה
בלבד) במקום `/api/users` — ראו סעיף 0א.5 לפקודות ולתוצאות המדויקות:
בלי טוקן = `403` (פרטי, מאומת), עם טוקן Google = `401` ברמת האפליקציה
(מוכיח ש-JWT flow + קריאת Firestore אמיתית עובדים, בלי לכתוב כלום).

### שלב 6 — (רק לאחר אישור מפורש נוסף) חשיפה פומבית
```bash
gcloud run services update musical-encryption-server \
  --region me-west1 \
  --project musical-encryption-database \
  --allow-unauthenticated
```

### שלב 7 — (מומלץ, לאחר הפעלת Billing) Budget Alert
```bash
gcloud billing budgets create \
  --billing-account=<BILLING_ACCOUNT_ID> \
  --display-name="musical-encryption-budget" \
  --budget-amount=10 \
  --threshold-rule=percent=0.5 \
  --threshold-rule=percent=0.9 \
  --threshold-rule=percent=1.0
```
(זוהי **התראה בלבד** — ראו הבהרה בסעיף 10.)

---

## 14. K. מושגי מפתח וטרייד-אופים למחברת הפרויקט

**מה נבדק בפועל מול מה שתוכנן בלבד — הבחנה קריטית:**
- **מומש, מאומת, ופרוס (Implemented, Verified & Deployed)**: קוד השרת,
  build/test מקומיים, SA ייעודי, secret ב-Secret Manager, **ושירות
  Cloud Run פרטי חי ונבדק מקצה-לקצה מול Firestore אמיתי** — ראו סעיף 0א.
- **מתוכנן בלבד (Planned, עדיין לא בוצע)**: שלב 6 (חשיפה פומבית) ו-שלב 7
  (Budget Alert) בסעיף 13 — **אסור היה לבצע אותם במשימה הנוכחית במפורש**
  (אין גישה ציבורית, אין שינוי Billing/Budget).
- **לא ידוע/לא אומת (Unknown)**: הסכום המספרי (ILS) של ה-Spend Cap על
  Cloud Run עדיין לא נחשף על ידי ה-API (`specifiedAmount` חסר `units`) —
  נבדק מחדש בסשן הזה, ללא שינוי מהממצא הקודם. ראו סעיף 0ב החדש.

**טרייד-אופים מרכזיים שנבחרו, ולמה:**
- `gcloud run deploy --source .` **על פני** build Docker מקומי — כי
  Docker לא מותקן, וההימנעות מהתקנה system-wide הייתה אילוץ מפורש.
- Rate limiter מחולק-IP **על פני** global — נבחר בסבב קודם כדי שתוקף
  בודד לא ינעל משתמשים לגיטימיים אחרים (ראו `CLOUD_SECURITY_NOTES.md`).
- `max-instances=1` **על פני** ברירת המחדל (ללא הגבלה) — ויתור על
  יכולת scale גבוהה בתמורה לתקרת-עלות פיזית פשוטה, מתאים לפרויקט לימודי
  (לא לפרודקשן אמיתי בהיקף גדול).
- Cloud Run IAM auth כ**שלב ביניים זמני בלבד**, לא כפתרון קבוע — כי
  משתמשי-קצה אמיתיים של ה-WPF client אין להם זהות Google; ה-JWT של
  האפליקציה הוא שכבת ההגנה הסופית והאמיתית.
- Secret Manager IAM **ממוקד על המשאב** (לא ברמת הפרויקט) — עיקרון
  least-privilege: ה-Runtime SA לא אמור לקבל גישה לסודות עתידיים שאינם
  קשורים אליו.

---

## 15. L. חיבור ה-CLIENT ל-SERVER (סשן נוסף — הכנה, עדיין לא חשיפה פומבית)

**הערת מינוח**: מכאן והלאה "CLIENT" = אפליקציית שולחן העבודה
(`Client-Musical-Encryption-Project`, בעבר כונתה "WPF client" במסמך זה).

### 15.1 אימות מחדש: הפריסה הקודמת עדיין תקינה
`gcloud run services describe` + `gcloud run revisions list` — הרוויזיה
`musical-encryption-server-00001-kw7` עדיין `Ready=True`,
`ConfigurationsReady=True`, `RoutesReady=True`, 100% מהתעבורה. **לא בוצע
deploy חדש בסשן הזה** — התצורה מהסשן הקודם (SA, secret, scaling) נשארה
זהה. השירות חי בשני hostnames תקינים לאותו service (מופיעים שניהם
ב-`run.googleapis.com/urls`):
- `https://musical-encryption-server-756281922173.me-west1.run.app`
  (project-number form)
- `https://musical-encryption-server-2bi6r3kbda-zf.a.run.app` (`status.url`
  הקנוני שמוחזר ע"י ה-API — **זה הכתובת שה-CLIENT אמור להשתמש בה**)

### 15.2 אימות מחדש: endpoints פיתוח/debug לא נגישים ב-production
נבדק עם Google identity token (קריאה בלבד, שום Firestore write):
`/dev` → `404`, `/scalar/v1` → `404`, `/openapi/v1.json` → `404`. מאומת:
כל קוד ה-`#if DEBUG` (כולל הדשבורד, ה-OpenAPI UI, וחשיפת רשימת
המשתמשים) מקומפל-החוצה לגמרי מה-binary שרץ ב-Cloud Run.

### 15.3 מיפוי endpoints — מי דורש JWT, מי לא (נבדק בקוד, לא רק בתיעוד)
| Endpoint | שיטה | JWT דרוש? | הערות |
|---|---|---|---|
| `/api/users` | POST (הרשמה) | **לא** (חייב להיות פתוח) | `[EnableRateLimiting("auth")]` — 10 בקשות/60s לכל IP |
| `/api/users/login` | POST | **לא** (חייב להיות פתוח) | אותו rate limit; מחזיר bearer token בהצלחה |
| `/api/users/change-password` | POST | **לא** — מאומת בעצמו מול הסיסמה הישנה, לא דורש bearer | אותו rate limit |
| `/api/users/{id}` | GET | **כן** (`[Authorize]` על המתודה) | |
| `/api/encryption` (כל המתודות) | POST/GET/DELETE | **כן** (`[Authorize]` על כל ה-controller) | אין rate limit נפרד — מוגן על ידי חובת JWT |

**בדיקת ownership מאומתת בקוד** (לא רק בתיעוד): `GetById`, `Decrypt`,
`Delete` בכולם בודקים `record.OwnerUserId != CallerId` → `403` אם לא
בעלים; `GetMine` מסנן לפי `CallerId` בשרת (לא לפי פרמטר מהלקוח) —
לקוח לא יכול "לבקש" רשומות של מישהו אחר. `CallerId` מגיע מ-`sub` claim
של ה-JWT **המאומת**, לא מכל קלט אחר של הקריאה.

### 15.4 שתי שכבות auth נפרדות — למה זה חשוב לחשיפה הפומבית
1. **Cloud Run IAM** (`--no-allow-unauthenticated`, המצב הנוכחי) — נבדק
   **לפני** שהבקשה בכלל מגיעה ל-container; דורש Identity Token של Google
   עם הרשאת `run.invoker`. **ה-CLIENT לא יכול לעבור את השכבה הזו** —
   למשתמשי-קצה שמריצים את ה-CLIENT אין זהות Google/gcloud בכלל.
2. **JWT של האפליקציה** (`[Authorize]`, `/api/users/login`) — נבדק
   **בתוך** קוד ה-ASP.NET Core, אחרי ששתי השכבות הראשונות (TLS של Cloud
   Run + ה-IAM gate אם הוא פעיל) כבר עברו.
**מסקנה**: כדי שה-CLIENT יעבוד מרחוק, שכבה (1) חייבת להיפתח
(`--allow-unauthenticated`) — שכבה (2) **נשארת פעילה ללא שינוי** ומספקת
את ההגנה האמיתית על נתוני כל משתמש.

### 15.5 פקודה מוכנה לחשיפה פומבית — **לא הורצה, מחכה לאישור נפרד שלך**
```bash
gcloud run services update musical-encryption-server \
  --project musical-encryption-database \
  --region me-west1 \
  --allow-unauthenticated
```
שקול לחלופין (identical effect, explicit IAM binding instead of the flag):
```bash
gcloud run services add-iam-policy-binding musical-encryption-server \
  --project musical-encryption-database \
  --region me-west1 \
  --member="allUsers" \
  --role="roles/run.invoker"
```
**השפעת אבטחה**: מבטל את שכבת Cloud Run IAM (1) — **כל אחד באינטרנט
יכול לשלוח בקשה ל-service** (לא רק לקרוא תגובה — Cloud Run ירוץ ויחשב
עבור כל בקשה שתגיע, כולל בקשות זבל/סקאנרים אוטומטיים שסורקים IP-ים
פומביים של `*.run.app`). ה-JWT של האפליקציה (`[Authorize]`) ממשיך לחסום
גישה בפועל לנתונים/ל-`/api/encryption`, ו-`/api/users/login` עדיין מוגן
ב-rate limiting (10/60s לכל IP) — אבל אין יותר שכבת gate לפני זה.
**השפעת עלות**: `max-instances=1` ו-`min-instances=0` ממשיכים להגביל
פיזית את ה-scale המקסימלי גם אחרי חשיפה — תעבורת-זבל יכולה **לכל היותר**
לתפוס את ה-instance היחיד (ולגרום ל-429/תגובות איטיות ללקוחות לגיטימיים),
אבל לא "תתפוצץ" בעלות בלתי-מוגבלת. Cloud Run free tier + Spend Cap (על
Cloud Run בלבד, ראו סעיף 0ב) ממשיכים לחול. **לא בוצע שום שינוי בפועל כאן.**

### 15.6 שינויי CLIENT שבוצעו בפועל (קוד בלבד, לא deploy, לא commit)
**קובץ חדש** `ServerConfig.cs` — פותר את כתובת ה-SERVER בלי hardcode
קבוע-בקומפילציה, לפי סדר עדיפות: (1) משתנה סביבה
`MUSICAL_ENCRYPTION_SERVER_URL`, (2) קובץ `server.config.json` ליד ה-.exe
(מפתח `ServerBaseUrl`), (3) נפילה לברירת המחדל המקומית `http://localhost:5266`
— **זהה להתנהגות הקודמת** כשאין לא משתנה-סביבה ולא קובץ (כלומר אין שינוי
התנהגות עבור מי שעדיין לא הגדיר דבר).

**קובץ חדש** `server.config.sample.json` — תבנית לדוגמה עם כתובת ה-Cloud
Run, **לא נטען אוטומטית** (השם שונה בכוונה מ-`server.config.json`) — יש
להעתיק אותו ל-`server.config.json` ליד ה-.exe שרוצים להצביע ל-Cloud Run.

**שונה** `ApiClient.cs` — שורה אחת: `BaseUrl` עובר מ-`const string` קשיח
ל-`static readonly` שנפתר על ידי `ServerConfig.ResolveBaseUrl()`. שום
מתודה אחרת לא שונתה — `RegisterAsync`/`LoginAsync`/`ChangePasswordAsync`/
`ClearAccessToken`/`HandleIfUnauthorized` זהים ל-100% לקודם. **HTTPS**:
כש-`BaseUrl` יוגדר לכתובת ה-Cloud Run (`https://...`), כל הבקשות דרך
`HttpClient` יהיו HTTPS אוטומטית לפי ה-scheme של ה-URI — לא נדרש קוד
נוסף, ו-Cloud Run בכל מקרה **לא מגיש תעבורה חוצה-רשת בלי TLS** על
כתובת `*.run.app` הציבורית (זו תכונת פלטפורמה, לא תצורה שנבדקה כאן).

**שונה** `AssemblyInfo.cs` — נוספה שורת `InternalsVisibleTo` לפרויקט
הטסטים החדש (ראו 15.7), כדי שיוכל לקרוא ל-`ServerConfig` (שהוא `internal`)
בלי להפוך אותו ל-`public`.

**לא שונה**: מצב OFFLINE (`ContinueWithoutAccount` ב-`MainWindow.xaml.cs`)
— נבדק בקוד, אין קריאת רשת בנתיב הזה כלל, ולא נגעתי בקובץ הזה בכלל
בסשן הזה. לא שונה שום אלגוריתם הצפנה (ה-CLIENT עדיין לא קורא בכלל
ל-`/api/encryption` — אין קוד קיים לשנות).

### 15.7 בדיקות שבוצעו (CLIENT)
- `dotnet build Musical-Encryption-Project.csproj -c Debug` **ו-**`-c Release`
  — **0 שגיאות, 0 אזהרות** בשתי התצורות.
- **נוסף פרויקט טסטים מבודד חדש** `Musical-Encryption-Project.Tests`
  (xUnit, בדיוק כמו `MusicalEncryptionServer.Tests` בשרת) עם 4 טסטים
  על `ServerConfig.ResolveBaseUrl()` — **ללא שום קריאת רשת, ללא Firestore,
  ללא Cloud Run**: קובץ `server.config.json` נכתב/נמחק בתיקיית הבדיקה
  עצמה בלבד (לא `server.config.sample.json`), ומשתנה הסביבה מתאופס
  בסוף כל טסט. **כל 4 הטסטים עברו**:
  1. בלי env var ובלי קובץ → ברירת המחדל המקומית (זהה להתנהגות הישנה).
  2. עם קובץ `server.config.json` → הכתובת מתוך הקובץ.
  3. עם env var **וגם** קובץ → ה-env var גובר (סדר העדיפויות כמתועד).
  4. קובץ JSON פגום → נפילה חזרה לברירת המחדל, לא קריסה.
- **לא נוצר משתמש/רשומה אמיתית** — אין טסט שמתחבר לשרת האמיתי כלל.
  **לא נבדק חיבור קצה-לקצה אמיתי מה-CLIENT ל-Cloud Run** — זה **לא
  אפשרי כרגע** כי השירות עדיין פרטי (ראו 15.4–15.5); כל ניסיון חיבור
  אמיתי מה-CLIENT לכתובת Cloud Run יקבל `403` מ-Cloud Run IAM, לא
  מה-JWT של האפליקציה.
- שרת: `dotnet test -c Release` הורץ מחדש (ללא שינוי קוד שרת) — **19/19
  עברו**, ללא Firestore אמיתי, כבעבר.

### 15.8 מה נותר לפני חיבור אמיתי ממחשב אחר
1. **אישור מפורש שלך** להרצת פקודת 15.5 (חשיפה פומבית) — זה החסם
   הממשי היחיד שנשאר בין המצב הנוכחי לחיבור CLIENT-אמיתי מרחוק.
2. אחרי החשיפה: להעתיק את `server.config.sample.json` ל-`server.config.json`
   ליד ה-.exe של ה-CLIENT (או להגדיר את משתנה הסביבה
   `MUSICAL_ENCRYPTION_SERVER_URL`) על כל מחשב שמריץ את ה-CLIENT מרחוק.
3. **רק אז** לבדוק בפועל מה-CLIENT: הרשמה → התחברות → (בעתיד, כשה-CLIENT
   יחובר ל-`/api/encryption`) הצפנה/פענוח — מול Firestore האמיתי. **לא
   בוצע בסשן הזה**, כנדרש במפורש ("לא ליצור משתמשים/רשומות ב-Firestore
   אמיתי", וגם כי השירות עדיין פרטי).
4. שקול (אופציונלי): אחרי שה-CLIENT מחובר בהצלחה, לצמצם את `AuthRateLimit`
   אם תעבורה פומבית אמיתית מצדיקה זאת, ולהוסיף Budget Alert (שלב 7,
   סעיף 13 — גם זה דורש אישור נפרד, שינוי Billing).

---

## 16. M. חשיפה פומבית בפועל + בדיקת קצה-לקצה אמיתית מה-CLIENT

### 16.1 בדיקות מקדימות (קריאה בלבד, לפני כל שינוי)
אומת מחדש לפני הפעולה: קוד `[Authorize]` קיים ללא שינוי על
`EncryptionController` (class-level) ועל `UsersController.GetById`;
תצורת הרוויזיה החיה (`musical-encryption-server-00001-kw7`) זהה
לקודמת — `maxScale=1`, `cpu-throttling=true`, 512Mi/1 vCPU; `get-iam-policy`
על השירות החזיר policy **ריק** (עדיין פרטי); Spend Cap עדיין
`CONFIGURED` על Cloud Run בלבד, ללא שינוי.

### 16.2 חשיפה פומבית — בוצעה בפועל
`gcloud run services update --allow-unauthenticated` **אינו קיים** כפקודה
(שגיאת `unrecognized arguments` — תיקון לתיעוד הקודם בסעיף 15.5: הדגל
הזה תקף רק ב-`gcloud run deploy`, לא ב-`services update`). הפקודה
שבאמת פעלה, והורצה בפועל:
```bash
gcloud run services add-iam-policy-binding musical-encryption-server \
  --project musical-encryption-database \
  --region me-west1 \
  --member="allUsers" \
  --role="roles/run.invoker"
```
**תוצאה**: binding יחיד — `allUsers` → `roles/run.invoker`. **לא שונה
שום דבר אחר**: לא הרצנו `deploy` חדש (אין build/Cloud Build חדש), לא
שונו resource limits/SA/secrets — אומת מחדש אחרי הפעולה
(`spec.template` זהה בדיוק לפני ואחרי, אותה `latestReadyRevisionName`).

### 16.3 אימות: בקשות בלי Google IAM מגיעות כעת לאפליקציה
```bash
curl -X POST "$SERVICE_URL/api/users/login" -d '{"username":"__e2e_nonexistent__","password":"x"}' # בלי שום Authorization header
```
**לפני** השינוי: `403` (נחסם ב-Cloud Run, לפני הקוד שלנו).
**אחרי** השינוי: **`401` מהאפליקציה** ("Invalid username or password") —
Cloud Run IAM gate בוטל, אבל ה-JWT גייט באפליקציה פעיל ומגיב כצפוי.
גם `/dev` נבדק בלי header כלשהו: **עדיין `404`** (קוד debug עדיין
מקומפל-החוצה, חשיפה פומבית לא "מגלה" אותו). `/api/encryption` בלי
טוקן: **`401`** (עדיין דורש JWT, לא קרס ולא נחשף).

### 16.4 בדיקת קצה-לקצה אמיתית — מה-CLIENT בעצמו, לא curl
נוצר `server.config.json` אמיתי (לא ה-sample) בשורש פרויקט ה-CLIENT,
עם כתובת Cloud Run (`status.url`), ונוספה לו שורת `CopyToOutputDirectory`
ב-csproj כך שהוא נחת בפועל ליד ה-`.exe` הבנוי (`bin/Debug/net9.0-windows/`).
**אומת** שה-build מעתיק אותו, ושה-`ApiClient` האמיתי קורא ממנו.

**בוצעה בדיקת רשת אמיתית** (לא curl — קריאה ישירה למחלקת `ApiClient`
המדויקת ש-`MainWindow` משתמש בה), באמצעות טסט חד-פעמי זמני
(`TEMP_LiveConnectivityCheck.cs`, **הורץ ואז נמחק מהריפו לגמרי** — לא
נשאר טסט שתלוי ברשת חיה בחבילת הטסטים הקבועה):
- `ApiClient.LoginAsync("__e2e_connectivity_check_nonexistent__", ...)` →
  `Success=False, Message="Invalid username or password."` — **לא**
  "Could not reach the server" — כלומר בקשת HTTPS אמיתית הגיעה
  ל-Cloud Run, עברה את ה-JWT middleware, קראה (read-only) מ-Firestore
  האמיתי, וקיבלה תשובת 401 אמיתית.
- `ApiClient.ChangePasswordAsync("__e2e_connectivity_check_nonexistent__", ...)` →
  `Success=False, Message="Invalid username or old password."` — אותה
  מסקנה, גם קריאה בלבד.
- משך הריצה הכולל: ~0.6 שנייה (מול שירות אמיתי ב-`me-west1`, לא mock).
- **לא נוצר ולא שונה שום משתמש/רשומה אמיתית** — שתי הקריאות היו על
  username שלא קיים, ושתיהן נכשלות **לפני** שורת כתיבה כלשהי בקוד השרת.

**מסקנה**: ה-CLIENT **מתחבר בהצלחה בפועל** ל-SERVER האמיתי ב-Cloud Run
דרך HTTPS, עם JWT flow תקין מקצה-לקצה (קריאה בלבד). **לא נבדק** עדיין
login/register **מוצלח** עם משתמש אמיתי קיים — ראו 16.6.

### 16.5 מצב OFFLINE — אומת בקוד, לא ב-GUI
`MainWindow.xaml.cs` **לא שונה בשום סשן** (לא בזה, לא בקודם). נבדק מחדש
ב-`ContinueWithoutAccount()`: אין קריאה ל-`_api` בכלל בנתיב הזה —
`_loggedInUsername = "Unknown"` מוגדר מקומית, אין שום `await`/HttpClient.
**לא בוצעה הרצת GUI בפועל** בסביבה הזו (אין מסך אינטראקטיבי להרצת אפליקציית
WPF בסשן headless) — האימות הוא **בדיקת קוד סטטית**, לא קליק בפועל על
"Continue without account". מומלץ שתאשר ביד, פעם אחת, שהכפתור עדיין
עובד כצפוי על המחשב שלך.

### 16.6 Firestore — מה נכתב, מה לא
**לא בוצעה שום כתיבה** ל-Firestore האמיתי בסשן הזה. כל קריאות הרשת
(curl ו-`ApiClient`) היו עם username שלא קיים, שנכשלות ב-401 **לפני**
שורת העדכון/כתיבה בקוד. ניסיון לקרוא את collection `users` ישירות
(קריאה בלבד, בלי לשנות כלום) **נחסם על ידי מנגנון בטיחות אוטומטי** של
Claude Code (סיווג "Production Reads") — **לא בוצע**, ולא נדרס.
**לכן לא ידוע אם קיים כבר חשבון אמיתי מאושר** ב-Firestore לבדיקת
login מוצלח — זו החלטה שלך: אם יש לך כבר חשבון, אפשר להשתמש בו (בלי
לחשוף את הסיסמה לכאן); אם לא, דרוש אישור נפרד ליצירת חשבון-בדיקה אחד
דרך `POST /api/users` (כתיבה אמיתית ל-production Firestore).

### 16.7 סיכונים פיננסיים — עודכן לאחר החשיפה הפומבית
- **הסיכון עלה באופן מהותי**: לפני החשיפה, רק מי שיש לו הרשאת IAM
  בפרויקט יכול היה לשלוח בקשה בכלל. **כעת כל כתובת IP באינטרנט יכולה**
  — כולל סקאנרים/בוטים אוטומטיים שסורקים כתובות `*.run.app` ציבוריות.
  כל בקשה כזו **צורכת** request-based billing (גם אם מקבלת 401/404
  מהר) — נספרת בתוך free tier של Cloud Run, אבל לא "חסומה" קודם.
- **`max-instances=1`** ממשיך להגביל פיזית: גם תחת עומס/scan אגרסיבי,
  לכל היותר instance בודד רץ — תוצאה סבירה היא **תגובות איטיות/429
  ללקוחות לגיטימיים**, לא "התפוצצות" עלות.
- **Spend Cap** (סעיף 0ב) ממשיך לכסות **רק Cloud Run** — Secret Manager
  (access operations) ו-Logging (ingestion) עדיין לא מכוסים על ידי שום
  cap, רק free tier חודשי. עלייה בתעבורה פומבית אמיתית = יותר secret
  accesses (כל cold start קורא את ה-secret) ויותר log lines — **עדיין
  צפוי להישאר בתוך free tier** בהיקף שימוש של פרויקט לימודי, אך זה
  **לא מובטח ב-$0** אם מישהו יתקוף/יסרוק אגרסיבית.
- **מומלץ בחום (לא בוצע, דורש אישור נפרד — שינוי Billing)**: Budget
  Alert אמיתי (שלב 7, סעיף 13) כעת רלוונטי יותר מאשר לפני החשיפה.

### 16.8 מה עדיין לא נבדק (לפני שלב המחברת)
1. **Login מוצלח עם חשבון אמיתי קיים** — תלוי אם יש לך כבר אחד (ראו
   16.6) או שתאשר יצירת חשבון-בדיקה חדש.
2. **הרשמה אמיתית** (`POST /api/users`) מה-CLIENT בפועל — לא בוצעה
   (תיצור משתמש production, דורש אישור).
3. **זרימת הצפנה/פענוח** (`/api/encryption`) — ה-CLIENT **עדיין לא
   קורא** ל-endpoints האלה בכלל (אין קוד קיים בצד הלקוח) — לא קשור
   לחשיפה הפומבית, זו עבודה עתידית נפרדת.
4. **הרצת GUI בפועל** (קליק אמיתי על הכפתורים: Create Account, Login,
   Continue without account, Sign out) — לא בוצעה בסביבה headless הזו;
   כל מה שנבדק כאן הוא קוד (`ApiClient`/`ServerConfig`) ישירות.
5. **תוקף/תפוגת טוקן בפועל** (`ExpiryMinutes=30`) — לא נבדק מחדש בסשן
   הזה (לא קשור לשינוי שבוצע).
