-- Add Cached column to Songs table
ALTER TABLE public."Songs" ADD COLUMN IF NOT EXISTS "Cached" boolean NOT NULL DEFAULT false;

-- Insert settings for cache path and download delay
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheStoragePath', '/var/bnkaraoke/cache');
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheDownloadDelaySeconds', '5');
-- Full path to the yt-dlp executable used for caching
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('YtDlpPath', '/usr/local/bin/yt-dlp');
-- Maximum allowed runtime for yt-dlp in seconds
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('YtDlpTimeout', '600');
