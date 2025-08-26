-- Add Cached column to Songs table
ALTER TABLE public."Songs" ADD COLUMN IF NOT EXISTS "Cached" boolean NOT NULL DEFAULT false;

-- Insert settings for cache path and download delay
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheStoragePath', '/var/bnkaraoke/cache');
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheDownloadDelaySeconds', '5');
