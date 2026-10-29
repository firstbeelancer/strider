-- Strider Mail SQLite Schema v2
-- Wave 6: coloured flags with rules, custom fonts and user text settings.
-- Applied automatically by DatabaseInitializer (version 2).

-- Coloured flag on a message (NULL = no flag).
ALTER TABLE messages ADD COLUMN flag_color TEXT;

-- Rules that colour incoming mail automatically (e.g. "all mail from boss = blue").
CREATE TABLE IF NOT EXISTS flag_rules (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL DEFAULT '',
    field TEXT NOT NULL DEFAULT 'sender',   -- sender | subject | recipient | any
    operator TEXT NOT NULL DEFAULT 'contains', -- contains | equals | startswith | regex
    pattern TEXT NOT NULL DEFAULT '',
    color TEXT NOT NULL DEFAULT 'blue',
    priority INTEGER NOT NULL DEFAULT 0,     -- lower value wins
    is_enabled INTEGER NOT NULL DEFAULT 1,
    created_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_flag_rules_priority ON flag_rules(is_enabled, priority);

-- Signature sent as a .html attachment instead of inline text (ТЗ F4.36).
ALTER TABLE signatures ADD COLUMN attach_as_html_file INTEGER NOT NULL DEFAULT 0;

-- Key/value store for user preferences (text style, fonts, composer options).
CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at INTEGER NOT NULL
);

-- Fonts the user added from a file (family name + where the file lives).
CREATE TABLE IF NOT EXISTS custom_fonts (
    id TEXT PRIMARY KEY,
    family TEXT NOT NULL UNIQUE,
    file_path TEXT NOT NULL,
    added_at INTEGER NOT NULL
);
