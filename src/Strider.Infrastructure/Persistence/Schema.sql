-- Strider Mail SQLite Schema v1
-- Run this to initialize the database

PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

-- Accounts
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

-- Folders
CREATE TABLE IF NOT EXISTS folders (
    id TEXT PRIMARY KEY,
    account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    remote_name TEXT NOT NULL,
    type TEXT NOT NULL DEFAULT 'custom',
    parent_id TEXT,
    last_sync_uid INTEGER NOT NULL DEFAULT 0,
    unread_count INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_folders_account ON folders(account_id);

-- Messages
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
CREATE INDEX IF NOT EXISTS idx_messages_folder ON messages(folder_id, date_utc DESC);
CREATE INDEX IF NOT EXISTS idx_messages_thread ON messages(thread_id);
CREATE INDEX IF NOT EXISTS idx_messages_unread ON messages(is_read) WHERE is_read = 0;
CREATE INDEX IF NOT EXISTS idx_messages_from ON messages(from_address);

-- Message bodies
CREATE TABLE IF NOT EXISTS message_bodies (
    message_id TEXT PRIMARY KEY REFERENCES messages(id) ON DELETE CASCADE,
    text_plain TEXT,
    text_html TEXT,
    raw_mime_path TEXT
);

-- Attachments
CREATE TABLE IF NOT EXISTS attachments (
    id TEXT PRIMARY KEY,
    message_id TEXT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    filename TEXT,
    content_type TEXT,
    size INTEGER NOT NULL DEFAULT 0,
    content_id TEXT,
    local_path TEXT
);
CREATE INDEX IF NOT EXISTS idx_attachments_message ON attachments(message_id);

-- Signatures
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
CREATE INDEX IF NOT EXISTS idx_signatures_account ON signatures(account_id);

-- Calendar events
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
CREATE INDEX IF NOT EXISTS idx_calendar_start ON calendar_events(start_utc);

-- PGP keys
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
CREATE INDEX IF NOT EXISTS idx_pgp_keys_account ON pgp_keys(account_id);

-- Pending operations (offline queue)
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
CREATE INDEX IF NOT EXISTS idx_pending_ops_status ON pending_ops(status);

-- AI settings
CREATE TABLE IF NOT EXISTS ai_settings (
    id TEXT PRIMARY KEY,
    provider TEXT NOT NULL,
    model TEXT NOT NULL,
    api_key_ref TEXT,
    base_url TEXT,
    is_default INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL
);
