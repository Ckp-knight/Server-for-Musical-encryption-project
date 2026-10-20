# Musical Encryption Server

A plain .NET 9 console app (generic host, no ASP.NET Core/Kestrel). It listens
for raw TCP connections, speaks a small length-prefixed JSON protocol, and
runs the exact same business logic (hashing, Firestore storage) that used to
sit behind HTTP controllers.

## Run

```bash
dotnet run
```

Listens on TCP port `5300`, on all local interfaces — any number of clients
can connect at once (see `Sockets/TcpSocketServer.cs`).

## Wire protocol

Every message, in both directions, is a 4-byte big-endian length prefix
followed by that many bytes of UTF-8 JSON (`Sockets/MessageProtocol.cs`):

- **Request**: `{ "Action": "register" | "login" | "changePassword", "Payload": {...} }`
- **Response**: `{ "Success": bool, "StatusCode": int, "Error": string?, "Payload": {...}? }`

`Sockets/RequestRouter.cs` maps `Action` to a `UserService` call. The CLIENT
project keeps a byte-for-byte identical copy of `MessageProtocol.cs` — if one
side changes the shape, the other must change too.

## Debugging / seeing what the server does

1. **Step-by-step debugger** — in VS Code press **F5** (needs the *C# Dev
   Kit* extension). Set breakpoints in `Sockets/ClientSession.cs` or
   `Services/UserService.cs` and step through line by line. Config is in
   `.vscode/launch.json`.
2. **Console log** — one line per accepted connection and per request
   (action + resulting status code), printed via the standard
   `Microsoft.Extensions.Logging` console logger.

## Project layout

| Folder / file | What it is |
| --- | --- |
| `Sockets/TcpSocketServer.cs` | `TcpListener` + accept loop; one task per connected client |
| `Sockets/ClientSession.cs` | Owns one client's connection: read request, route it, write response |
| `Sockets/RequestRouter.cs` | Maps an `Action` string to a `UserService` call |
| `Sockets/MessageProtocol.cs` | The framing + request/response envelope shared with the CLIENT |
| `Services/UserService.cs` | Register / login / change-password business logic (no transport-specific code) |
| `Services/IMusicalCipher.cs` | The encryption algorithm, behind an interface |
| `Services/PlaceholderMusicalCipher.cs` | **Temporary** XOR+Base64 so it runs. Replace with the real algorithm |
| `Services/Pbkdf2PasswordHasher.cs` | Hashes/verifies passwords (PBKDF2, built into .NET) |
| `Repositories/IEncryptionRepository.cs` / `FirestoreEncryptionRepository.cs` | Encryption record storage (not yet wired to a socket action) |
| `Repositories/IUserRepository.cs` / `FirestoreUserRepository.cs` | User storage, permanent (Google Cloud Firestore) |
| `Models/User.cs` | A registered account — password stored only as a salted hash |
| `Models/EncryptionRecord.cs` | The stored encryption record shape (one per record) |
| `Program.cs` | Wires everything together (dependency injection) and starts the host |

## Actions implemented over the socket protocol

Only the three flows the WPF CLIENT actually calls today:

| Action | Payload | Purpose |
| --- | --- | --- |
| `register` | `{ Username, Password }` | Create a new account |
| `login` | `{ Username, Password }` | Check username + password |
| `changePassword` | `{ Username, OldPassword, NewPassword }` | Change an account's password |

The Encryption repository/cipher/DTOs still exist (`Repositories/IEncryptionRepository.cs`,
`Services/IMusicalCipher.cs`, `Models/Dtos.cs`) but have no socket action wired
to them yet — the CLIENT has no UI for them either. Adding one later means:
add a method to a new `EncryptionService`, add a `case` in `RequestRouter`,
no changes to the socket plumbing itself.
