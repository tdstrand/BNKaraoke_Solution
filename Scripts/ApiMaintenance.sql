-- Add Cached column to Songs table
ALTER TABLE public."Songs" ADD COLUMN IF NOT EXISTS "Cached" boolean NOT NULL DEFAULT false;
-- Add Mature column to Songs table
ALTER TABLE public."Songs" ADD COLUMN IF NOT EXISTS "Mature" boolean NOT NULL DEFAULT false;

-- Add ApprovedDate column to Songs table
ALTER TABLE public."Songs" ADD COLUMN IF NOT EXISTS "ApprovedDate" timestamptz;

-- Insert settings for cache path and download delay
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheStoragePath', '/var/bnkaraoke/cache');
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('CacheDownloadDelaySeconds', '5');
-- Full path to the yt-dlp executable used for caching
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('YtDlpPath', '/usr/local/bin/yt-dlp');
-- Maximum allowed runtime for yt-dlp in seconds
INSERT INTO public."ApiSettings" ("SettingKey", "SettingValue") VALUES ('YtDlpTimeout', '600');

-- Queue reorder persistence
CREATE TABLE IF NOT EXISTS public."QueueReorderPlans"
(
    "PlanId" uuid PRIMARY KEY,
    "EventId" integer NOT NULL,
    "BasedOnVersion" character varying(128) NOT NULL,
    "ProposedVersion" character varying(128) NOT NULL,
    "MaturePolicy" character varying(32) NOT NULL,
    "MoveCount" integer NOT NULL DEFAULT 0,
    "PlanJson" jsonb NOT NULL,
    "MetadataJson" jsonb,
    "CreatedBy" character varying(128),
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ExpiresAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS "IX_QueueReorderPlans_EventId_BasedOnVersion"
    ON public."QueueReorderPlans" ("EventId", "BasedOnVersion");

CREATE TABLE IF NOT EXISTS public."QueueReorderAudits"
(
    "AuditId" uuid PRIMARY KEY,
    "EventId" integer NOT NULL,
    "PlanId" uuid,
    "Action" character varying(64) NOT NULL,
    "UserName" character varying(128),
    "MaturePolicy" character varying(32),
    "PayloadJson" jsonb,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "FK_QueueReorderAudits_Plans" FOREIGN KEY ("PlanId")
        REFERENCES public."QueueReorderPlans" ("PlanId") ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS "IX_QueueReorderAudits_EventId_CreatedAt"
    ON public."QueueReorderAudits" ("EventId", "CreatedAt");
