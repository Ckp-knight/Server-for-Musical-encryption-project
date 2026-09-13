namespace MusicalEncryptionServer.Diagnostics;

/// <summary>
/// The dev-only "control panel" page at /dev — a live feed of requests in/out
/// plus the list of registered users. Plain HTML+JS, no build step; it polls
/// its own JSON endpoints (/dev/requests, /dev/users) once a second.
/// </summary>
public static class DevDashboardHtml
{
    public const string Content = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8" />
        <title>Server Control Panel</title>
        <style>
          :root { color-scheme: dark; }
          * { box-sizing: border-box; }
          body { margin:0; font-family: Consolas, "Segoe UI", monospace; background:#0f1115; color:#d7dae0; }
          header { padding:14px 18px; border-bottom:1px solid #262a33; display:flex; align-items:center; gap:10px; }
          header h1 { font-size:15px; margin:0; font-weight:600; color:#fff; }
          header .dot { width:8px; height:8px; border-radius:50%; background:#2e7d32; box-shadow:0 0 6px #2e7d32; flex:none; }
          header .sub { color:#6b7280; font-size:12px; margin-left:auto; }
          main { display:flex; height:calc(100vh - 50px); }
          section { overflow:auto; }
          #log { flex: 2; border-right:1px solid #262a33; padding:6px 0; }
          #users { flex: 1; min-width:220px; padding:14px; }
          #users h2 { font-size:12px; text-transform:uppercase; letter-spacing:.06em; color:#8a93a3; margin:0 0 10px; display:flex; justify-content:space-between; align-items:center; }
          .row { display:grid; grid-template-columns: 78px 46px 1fr 56px 110px; gap:8px; padding:4px 16px; font-size:12.5px; align-items:center; border-bottom:1px solid #181b21; }
          .row .time { color:#6b7280; }
          .row .method { font-weight:700; }
          .row .path { color:#c7cbd4; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
          .row .meta { color:#8a93a3; text-align:right; white-space:nowrap; }
          .badge { padding:1px 6px; border-radius:4px; font-weight:700; text-align:center; font-size:11.5px; }
          .ok { background:#123a1c; color:#4caf50; }
          .warn { background:#3a2f12; color:#e0a72e; }
          .err { background:#3a1414; color:#e05252; }
          .userrow { display:flex; align-items:center; justify-content:space-between; padding:6px 4px 6px 0; border-bottom:1px solid #181b21; font-size:12.5px; gap:8px; }
          .userrow .info { display:flex; flex-direction:column; gap:1px; min-width:0; }
          .userrow .name { color:#fff; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
          .userrow .date { color:#6b7280; font-size:11px; white-space:nowrap; }
          .empty { padding:16px; color:#6b7280; font-size:12.5px; }
          button { background:#1b1e26; border:1px solid #2b2f3a; color:#d7dae0; padding:3px 9px; border-radius:5px; font-size:11.5px; cursor:pointer; }
          button:hover { border-color:#4a4f5c; }
          .trash-btn { flex:none; display:inline-flex; align-items:center; justify-content:center; background:transparent; border:1px solid transparent; color:#6b7280; padding:5px; border-radius:6px; cursor:pointer; opacity:0; transition: opacity .12s ease, color .15s ease, background-color .15s ease, border-color .15s ease; }
          .userrow:hover .trash-btn { opacity:1; }
          .trash-btn:hover { color:#e05252; background:#3a1414; border-color:#5c2323; }
        </style>
        </head>
        <body>
          <header>
            <span class="dot"></span>
            <h1>Musical Encryption Server — Control Panel</h1>
            <span class="sub">requests reset on restart (in-memory) · updates every second</span>
          </header>
          <main>
            <section id="log"></section>
            <section id="users">
              <h2>Registered users <button onclick="loadUsers()">refresh</button></h2>
              <div id="usersList"></div>
            </section>
          </main>
        <script>
        function badgeClass(status) {
          if (status >= 500 || status === 401 || status === 403) return 'err';
          if (status >= 400) return 'warn';
          return 'ok';
        }
        function fmtTime(iso) {
          return new Date(iso).toLocaleTimeString();
        }
        async function loadLog() {
          const log = document.getElementById('log');
          try {
            const res = await fetch('/dev/requests');
            const items = await res.json();
            if (items.length === 0) {
              log.innerHTML = '<div class="empty">No requests yet — try Scalar (/scalar/v1) or the WPF app.</div>';
              return;
            }
            log.innerHTML = items.map(r => `
              <div class="row">
                <span class="time">${fmtTime(r.timestamp)}</span>
                <span class="method">${r.method}</span>
                <span class="path" title="${r.path}">${r.path}</span>
                <span><span class="badge ${badgeClass(r.statusCode)}">${r.statusCode}</span></span>
                <span class="meta">${r.userId ?? '—'} · ${r.elapsedMs}ms</span>
              </div>`).join('');
          } catch (e) {
            log.innerHTML = '<div class="empty">Could not reach the server.</div>';
          }
        }
        const TRASH_ICON = `
          <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor"
               stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="3 6 5 6 21 6"></polyline>
            <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"></path>
            <path d="M10 11v6"></path>
            <path d="M14 11v6"></path>
            <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"></path>
          </svg>`;

        async function loadUsers() {
          const el = document.getElementById('usersList');
          try {
            const res = await fetch('/dev/users');
            const items = await res.json();
            el.innerHTML = '';
            if (items.length === 0) {
              el.innerHTML = '<div class="empty">No users registered yet.</div>';
              return;
            }
            // Built with createElement (not innerHTML + string interpolation) so a
            // username can never be interpreted as HTML/script — it's just text.
            for (const u of items) {
              const row = document.createElement('div');
              row.className = 'userrow';

              const info = document.createElement('span');
              info.className = 'info';
              const name = document.createElement('span');
              name.className = 'name';
              name.textContent = u.username;
              const date = document.createElement('span');
              date.className = 'date';
              date.textContent = new Date(u.createdAt).toLocaleString();
              info.append(name, date);

              const trash = document.createElement('button');
              trash.className = 'trash-btn';
              trash.title = 'Delete ' + u.username;
              trash.innerHTML = TRASH_ICON;
              trash.addEventListener('click', () => deleteUser(u.id, u.username));

              row.append(info, trash);
              el.appendChild(row);
            }
          } catch (e) {
            el.innerHTML = '<div class="empty">Could not reach the server.</div>';
          }
        }

        async function deleteUser(id, username) {
          if (!confirm(`Delete user "${username}"? This cannot be undone.`)) return;
          try {
            const res = await fetch('/dev/users/' + encodeURIComponent(id), { method: 'DELETE' });
            if (res.ok || res.status === 404) {
              loadUsers();
            } else {
              alert('Could not delete: server returned ' + res.status);
            }
          } catch (e) {
            alert('Could not reach the server.');
          }
        }

        loadLog();
        loadUsers();
        setInterval(loadLog, 1000);
        </script>
        </body>
        </html>
        """;
}
