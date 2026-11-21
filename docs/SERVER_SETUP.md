# Running the ChatApp server (Developer A's PC)

Companion to `POSTGRESQL_SETUP.md` (database) and `PROTOCOL_ASSUMPTIONS.md` (wire format).

## 1. One-time

1. Do POSTGRESQL_SETUP.md section 1 (role `chatapp_user`, database `chatapp`).
2. Run `docs/postgres_schema.sql` **connected as `chatapp_user`** (pgAdmin: register a second server connection with that
   login, or `psql -h localhost -U chatapp_user -d chatapp -f docs/postgres_schema.sql`).
   If you run it as `postgres`, the tables belong to `postgres` and the server fails with "permission denied for table".
   Already did that? Fix with:
   `ALTER TABLE users, friendships, conversations, conversation_members, messages, posts, post_reactions, post_comments OWNER TO chatapp_user;`
   (run as `postgres` on the `chatapp` database).
3. Set the connection string (POSTGRESQL_SETUP.md section 4), then restart Visual Studio:
   `setx CHAT_DB "Host=localhost;Port=5432;Database=chatapp;Username=chatapp_user;Password=..."`
   Use the same port your PostgreSQL really listens on (the old code used 5433).

## 2. Let Developer B reach the server (TCP 5000)

The plan only opens PostgreSQL (5432). The CLIENT connects to the SERVER, so this port is the one B needs.
PowerShell as administrator, private networks only (use your subnet):

```powershell
New-NetFirewallRule -DisplayName "ChatApp server (LAN)" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private -RemoteAddress 192.168.1.0/24
```

Test from B's PC: `Test-NetConnection <A's IP> -Port 5000` must show `TcpTestSucceeded : True` (while the server runs).
Never forward port 5000 on the router. To use another port: `setx CHAT_PORT 5001` (and open that port instead).

## 3. Run

Visual Studio: set the server project (`chat`) as startup project and press F5. Expected console output:

```
[Server] PostgreSQL OK
[Server] Listening on TCP 5000. Press Ctrl+C to stop.
```

* `Environment variable CHAT_DB is not set` -> redo step 1.3 and restart Visual Studio.
* `[DB] ping failed: ...` and "Cannot reach PostgreSQL" -> see the table in POSTGRESQL_SETUP.md section 6.

## 4. Smoke test with the client

1. Start the client, untick "Demo mode", server `127.0.0.1:5000`.
2. Register `alice`, log in. On B's PC register `bob` (server `<A's IP>:5000`).
3. alice -> add friend `bob`; bob accepts; open a chat and send a message both ways; close bob's client and check that
   alice sees him go offline.
4. In pgAdmin: `SELECT * FROM messages ORDER BY id DESC;` shows the messages.
