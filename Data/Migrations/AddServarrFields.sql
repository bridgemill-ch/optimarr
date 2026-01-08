-- Migration: Add Servarr fields to VideoAnalysis table
-- Run this script if you have an existing database to add the new Servarr integration fields

-- Note: SQLite has limited ALTER TABLE support, so we need to:
-- 1. Create a new table with the new structure
-- 2. Copy data from old table
-- 3. Drop old table
-- 4. Rename new table

BEGIN TRANSACTION;

-- Create new table with Servarr fields
CREATE TABLE VideoAnalyses_new (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FilePath TEXT NOT NULL,
    FileName TEXT NOT NULL DEFAULT '',
    FileSize INTEGER NOT NULL DEFAULT 0,
    Duration REAL NOT NULL DEFAULT 0,
    Container TEXT NOT NULL DEFAULT '',
    VideoCodec TEXT NOT NULL DEFAULT '',
    VideoCodecTag TEXT NOT NULL DEFAULT '',
    IsCodecTagCorrect INTEGER NOT NULL DEFAULT 1,
    BitDepth INTEGER NOT NULL DEFAULT 8,
    Width INTEGER NOT NULL DEFAULT 0,
    Height INTEGER NOT NULL DEFAULT 0,
    FrameRate REAL NOT NULL DEFAULT 0,
    IsHDR INTEGER NOT NULL DEFAULT 0,
    HDRType TEXT NOT NULL DEFAULT '',
    IsFastStart INTEGER NOT NULL DEFAULT 0,
    AudioCodecs TEXT NOT NULL DEFAULT '',
    AudioTrackCount INTEGER NOT NULL DEFAULT 0,
    AudioTracksJson TEXT NOT NULL DEFAULT '',
    SubtitleFormats TEXT NOT NULL DEFAULT '',
    SubtitleTrackCount INTEGER NOT NULL DEFAULT 0,
    SubtitleTracksJson TEXT NOT NULL DEFAULT '',
    OverallScore TEXT NOT NULL DEFAULT 'Unknown',
    CompatibilityRating INTEGER NOT NULL DEFAULT 0,
    DirectPlayClients INTEGER NOT NULL DEFAULT 0,
    RemuxClients INTEGER NOT NULL DEFAULT 0,
    TranscodeClients INTEGER NOT NULL DEFAULT 0,
    Issues TEXT NOT NULL DEFAULT '',
    Recommendations TEXT NOT NULL DEFAULT '',
    ClientResults TEXT NOT NULL DEFAULT '',
    FullReport TEXT NOT NULL DEFAULT '',
    IsBroken INTEGER NOT NULL DEFAULT 0,
    BrokenReason TEXT,
    AnalyzedAt TEXT NOT NULL,
    LibraryScanId INTEGER,
    -- New Servarr fields
    ServarrType TEXT,
    SonarrSeriesId INTEGER,
    SonarrSeriesTitle TEXT,
    SonarrEpisodeId INTEGER,
    SonarrEpisodeNumber INTEGER,
    SonarrSeasonNumber INTEGER,
    RadarrMovieId INTEGER,
    RadarrMovieTitle TEXT,
    RadarrYear INTEGER,
    ServarrMatchedAt TEXT,
    FOREIGN KEY (LibraryScanId) REFERENCES LibraryScans(Id) ON DELETE CASCADE
);

-- Copy data from old table
INSERT INTO VideoAnalyses_new 
SELECT 
    Id, FilePath, FileName, FileSize, Duration, Container, VideoCodec, VideoCodecTag,
    IsCodecTagCorrect, BitDepth, Width, Height, FrameRate, IsHDR, HDRType, IsFastStart,
    AudioCodecs, AudioTrackCount, AudioTracksJson, SubtitleFormats, SubtitleTrackCount,
    SubtitleTracksJson, OverallScore, CompatibilityRating, DirectPlayClients, RemuxClients,
    TranscodeClients, Issues, Recommendations, ClientResults, FullReport, IsBroken, BrokenReason,
    AnalyzedAt, LibraryScanId,
    -- New fields default to NULL
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
FROM VideoAnalyses;

-- Drop old table
DROP TABLE VideoAnalyses;

-- Rename new table
ALTER TABLE VideoAnalyses_new RENAME TO VideoAnalyses;

-- Recreate indexes
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_FilePath ON VideoAnalyses(FilePath);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_LibraryScanId ON VideoAnalyses(LibraryScanId);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_AnalyzedAt ON VideoAnalyses(AnalyzedAt);
CREATE INDEX IF NOT EXISTS IX_VideoAnalyses_OverallScore ON VideoAnalyses(OverallScore);

COMMIT;
