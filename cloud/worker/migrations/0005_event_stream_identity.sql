-- A persistent identity detects switching servers or restoring/resetting D1.
INSERT INTO cloud_sync_meta(key, value) VALUES ('event_stream_id', lower(hex(randomblob(16))));
