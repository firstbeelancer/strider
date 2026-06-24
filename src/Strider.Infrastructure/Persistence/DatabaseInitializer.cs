using Microsoft.Data.Sqlite;

namespace Strider.Infrastructure.Persistence;

/// <summary>
/// Initializes SQLite database with schema.
/// </summary>
public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Persistence", "Schema.sql");
        
        // Fallback: read from embedded resource or inline
        string schema;
        if (File.Exists(schemaPath))
        {
            schema = await File.ReadAllTextAsync(schemaPath, ct);
        }
        else
        {
            schema = GetEmbeddedSchema();
        }

        var command = connection.CreateCommand();
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string GetEmbeddedSchema()
    {
        // Minimal inline schema as fallback
        return @"
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            
            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT PRIMARY KEY,
                email TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL DEFAULT '',
                imap_host TEXT NOT NULL DEFAULT '',
                imap_port INTEGER NOT NULL DEFAULT 993,
                imap_use_ssl INTEGER NOT NULL DEFAULT 1,
                smtp_host TEXT NOT NULL DEFAULT '',
                smtp_port INTEGER NOT NULL DEFAULT 587,
                smtp_use_ssl INTEGER NOT NULL DEFAULT 1,
                oauth2_token_ref TEXT,
                sync_state TEXT,
                default_signature_id TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS folders (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
                remote_name TEXT NOT NULL,
                type TEXT NOT NULL DEFAULT 'custom',
                parent_id TEXT,
                last_sync_uid INTEGER NOT NULL DEFAULT 0,
                unread_count INTEGER NOT NULL DEFAULT 0
            );
            
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                folder_id TEXT NOT NULL,
                message_uid INTEGER NOT NULL,
                message_id TEXT,
                in_reply_to TEXT,
                references_json TEXT,
                from_address TEXT NOT NULL DEFAULT '',
                from_name TEXT,
                to_addresses TEXT NOT NULL DEFAULT '[]',
                cc_addresses TEXT,
                subject TEXT NOT NULL DEFAULT '',
                date_utc INTEGER,
                size INTEGER NOT NULL DEFAULT 0,
                has_attachments INTEGER NOT NULL DEFAULT 0,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_starred INTEGER NOT NULL DEFAULT 0,
                is_flagged INTEGER NOT NULL DEFAULT 0,
                thread_id TEXT,
                ai_category TEXT,
                ai_summary TEXT,
                pgp_status TEXT NOT NULL DEFAULT 'none',
                pgp_verified TEXT NOT NULL DEFAULT 'unknown',
                fetched_at INTEGER NOT NULL,
                UNIQUE(account_id, folder_id, message_uid)
            );
            
            CREATE TABLE IF NOT EXISTS message_bodies (
                message_id TEXT PRIMARY KEY REFERENCES messages(id) ON DELETE CASCADE,
                text_plain TEXT,
                text_html TEXT,
                raw_mime_path TEXT
            );
            
            CREATE TABLE IF NOT EXISTS attachments (
                id TEXT PRIMARY KEY,
                message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
                filename TEXT,
                content_type TEXT,
                size INTEGER NOT NULL DEFAULT 0,
                content_id TEXT,
                local_path TEXT
            );
            
            CREATE TABLE IF NOT EXISTS signatures (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                content_html TEXT,
                content_plain TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS calendar_events (
                id TEXT PRIMARY KEY,
                account_id TEXT,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start_utc INTEGER NOT NULL,
                end_utc INTEGER NOT NULL,
                all_day INTEGER NOT NULL DEFAULT 0,
                color TEXT,
                reminder_minutes INTEGER,
                recurrence_rule TEXT,
                caldav_uid TEXT,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS pgp_keys (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
                key_id TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                public_key_armored TEXT NOT NULL,
                private_key_armored TEXT,
                user_id TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS pending_ops (
                id TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                op_type TEXT NOT NULL,
                payload TEXT NOT NULL DEFAULT '{}',
                status TEXT NOT NULL DEFAULT 'pending',
                retry_count INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS ai_settings (
                id TEXT PRIMARY KEY,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                api_key_ref TEXT,
                base_url TEXT,
                is_default INTEGER NOT NULL DEFAULT 0,
                created_at INTEGER NOT NULL
            );
        ";
    }
}
