# Musical Encryption Server

ASP.NET Core (.NET 9) Web API. The server encrypts text, stores records, and
enforces per-user permissions. Clients never talk to the database directly.

## Run

```bash
dotnet run
```

The client identifies itself with an `X-User-Id` header (temporary — see TODO).

## Debugging / seeing what the server does

1. **Interactive API UI (Scalar)** — while the server runs (Development), open
   `http://localhost:5266/scalar/v1`. Every endpoint is listed; fill in the body
   and the `X-User-Id` header, press Send, see the response. Raw OpenAPI JSON is
   at `/openapi/v1.json`.
2. **Step-by-step debugger** — in VS Code press **F5** (needs the *C# Dev Kit*
   extension). Set breakpoints in `EncryptionController.cs` and step through line
   by line, inspecting every variable. Config is in `.vscode/launch.json`.
3. **Request log** — the console prints one line in and one line out for every
   request (method, path, user, status, milliseconds). See
   `Middleware/RequestLoggingMiddleware.cs`.
4. `MusicalEncryptionServer.http` — send the sample requests straight from VS Code.

## Project layout

| Folder / file | What it is |
| --- | --- |
| `Controllers/EncryptionController.cs` | HTTP endpoints, validation, permission checks |
| `Services/IMusicalCipher.cs` | The encryption algorithm, behind an interface |
| `Services/PlaceholderMusicalCipher.cs` | **Temporary** XOR+Base64 so it runs. Replace with the real algorithm |
| `Repositories/IEncryptionRepository.cs` | What the server needs from the database |
| `Repositories/InMemoryEncryptionRepository.cs` | Current storage — a dictionary, lost on restart |
| `Repositories/FirestoreEncryptionRepository.cs` | Firestore storage — **not implemented yet**, step-by-step notes inside |
| `Models/EncryptionRecord.cs` | The stored document (one per record) |
| `Models/User.cs` | A registered account — password stored only as a salted hash |
| `Services/Pbkdf2PasswordHasher.cs` | Hashes/verifies passwords (PBKDF2, built into .NET) |
| `Repositories/IUserRepository.cs` / `InMemoryUserRepository.cs` | Same in-memory-now pattern as the encryption records |
| `Controllers/UsersController.cs` | Register + login |
| `Middleware/RequestLoggingMiddleware.cs` | Logs every request (method, user, status, timing) |
| `Program.cs` | Wires everything together (dependency injection) |

## Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `POST` | `/api/users` | Register a new account |
| `POST` | `/api/users/login` | Check username+password |
| `POST` | `/api/encryption` | Encrypt + store, returns new id |
| `GET` | `/api/encryption` | List my records (summaries) |
| `GET` | `/api/encryption/{id}` | Get one full record (owner only) |
| `POST` | `/api/encryption/{id}/decrypt` | Decrypt with a key (owner only) |
| `DELETE` | `/api/encryption/{id}` | Delete (owner only) |

## Switching to Firestore later

1. `dotnet add package Google.Cloud.Firestore`
2. Create the Firebase project, download the service-account JSON key (keep it
   out of git — `.gitignore` already blocks common names).
3. Set env var `GOOGLE_APPLICATION_CREDENTIALS` to the key file path.
4. Put the project id in `appsettings.json` -> `Firestore:ProjectId`.
5. Implement the four methods in `FirestoreEncryptionRepository.cs`.
6. In `Program.cs`, change the one registration line from
   `InMemoryEncryptionRepository` to `FirestoreEncryptionRepository`.

Nothing else in the project changes.
