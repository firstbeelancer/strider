-- Strider Mail SQLite Schema v3
-- Wave 7: server reconciliation state, and the automatic-flag marker that Wave 6 kept in memory only.
-- No semicolons in comments: DatabaseInitializer splits statements on them.

-- UIDVALIDITY of the folder at the last sync (0 = not known yet).
ALTER TABLE folders ADD COLUMN uid_validity INTEGER NOT NULL DEFAULT 0;

-- 1 when flag_color came from a rule, so a rule change can repaint it. Manual colours stay 0.
ALTER TABLE messages ADD COLUMN flag_color_is_automatic INTEGER NOT NULL DEFAULT 0;
