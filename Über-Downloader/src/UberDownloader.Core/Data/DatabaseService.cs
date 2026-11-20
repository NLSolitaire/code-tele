using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using UberDownloader.Core.Models;
using UberDownloader.Core.Services;

namespace UberDownloader.Core.Data;

public enum DateFilter
{
    All,
    Today,
    Last7Days,
    ThisMonth
}

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        string dbPath = Path.Combine(AppPaths.DataDirectory, "uber_downloader.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS Downloads (
                Id TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                TargetUrl TEXT,
                Title TEXT,
                CustomTitle TEXT,
                Quality TEXT,
                Codec TEXT,
                Status INTEGER NOT NULL,
                StatusDetail TEXT,
                Progress REAL DEFAULT 0,
                DownloadedBytes INTEGER DEFAULT 0,
                TotalBytes INTEGER DEFAULT 0,
                SpeedStr TEXT,
                EtaStr TEXT,
                SizeStr TEXT,
                OutputPath TEXT,
                TargetFolder TEXT,
                ThumbnailPath TEXT,
                ErrorMessage TEXT,
                IsLive INTEGER DEFAULT 0,
                LiveDurationStr TEXT,
                CreatedAt TEXT NOT NULL,
                FinishedAt TEXT,
                ScheduleType TEXT,
                ScheduledTime TEXT,
                AudioLanguage TEXT,
                SubtitleTrack TEXT,
                VideoResolution TEXT,
                VideoFps TEXT,
                VideoCodec TEXT,
                VideoDynamicRange TEXT,
                ContainerFormat TEXT,
                SpeedLimit TEXT,
                AudioCodec TEXT,
                AudioBitrate TEXT,
                AudioSampleRate TEXT,
                AudioBitDepth TEXT,
                AudioChannels TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_Downloads_Status ON Downloads(Status);
            CREATE INDEX IF NOT EXISTS IX_Downloads_CreatedAt ON Downloads(CreatedAt);

            CREATE TABLE IF NOT EXISTS Playlists (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                SourceUrl TEXT NOT NULL,
                TargetFolder TEXT,
                Status INTEGER NOT NULL,
                TotalItems INTEGER DEFAULT 0,
                CustomTitle TEXT,
                CreatedAt TEXT NOT NULL,
                FinishedAt TEXT,
                NamingTemplate TEXT,
                PreferredQuality TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_Playlists_CreatedAt ON Playlists(CreatedAt);
        ";
        cmd.ExecuteNonQuery();

        // Migrations douces pour bases de données existantes
        string[] newCols = {
            "AudioLanguage", "SubtitleTrack", "VideoResolution", "VideoFps",
            "VideoCodec", "VideoDynamicRange", "ContainerFormat", "SpeedLimit",
            "AudioCodec", "AudioBitrate", "AudioSampleRate", "AudioBitDepth", "AudioChannels",
            "EmbedSubtitles", "PlaylistId",
            "PlaylistIndex", "PlaylistFolder", "Attempts", "LastError", "NamingTemplate",
            "TargetFolder"
        };
        foreach (var col in newCols)
        {
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE Downloads ADD COLUMN {col} TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch { }
        }

        string[] plCols = { "NamingTemplate", "PreferredQuality" };
        foreach (var col in plCols)
        {
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE Playlists ADD COLUMN {col} TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public async Task UpsertItemAsync(DownloadItem item)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Downloads (
                Id, Url, TargetUrl, Title, CustomTitle, Quality, Codec,
                Status, StatusDetail, Progress, DownloadedBytes, TotalBytes,
                SpeedStr, EtaStr, SizeStr, OutputPath, ThumbnailPath,
                ErrorMessage, IsLive, LiveDurationStr, CreatedAt, FinishedAt,
                ScheduleType, ScheduledTime, AudioLanguage, SubtitleTrack,
                VideoResolution, VideoFps, VideoCodec, VideoDynamicRange,
                ContainerFormat, SpeedLimit, AudioCodec, AudioBitrate, AudioSampleRate, AudioBitDepth, AudioChannels,
                EmbedSubtitles, TargetFolder,
                PlaylistId, PlaylistIndex, PlaylistFolder, Attempts, LastError, NamingTemplate
            ) VALUES (
                $Id, $Url, $TargetUrl, $Title, $CustomTitle, $Quality, $Codec,
                $Status, $StatusDetail, $Progress, $DownloadedBytes, $TotalBytes,
                $SpeedStr, $EtaStr, $SizeStr, $OutputPath, $ThumbnailPath,
                $ErrorMessage, $IsLive, $LiveDurationStr, $CreatedAt, $FinishedAt,
                $ScheduleType, $ScheduledTime, $AudioLanguage, $SubtitleTrack,
                $VideoResolution, $VideoFps, $VideoCodec, $VideoDynamicRange,
                $ContainerFormat, $SpeedLimit, $AudioCodec, $AudioBitrate, $AudioSampleRate, $AudioBitDepth, $AudioChannels,
                $EmbedSubtitles, $TargetFolder,
                $PlaylistId, $PlaylistIndex, $PlaylistFolder, $Attempts, $LastError, $NamingTemplate
            )
            ON CONFLICT(Id) DO UPDATE SET
                Url = excluded.Url,
                TargetUrl = excluded.TargetUrl,
                Title = excluded.Title,
                CustomTitle = excluded.CustomTitle,
                Quality = excluded.Quality,
                Codec = excluded.Codec,
                Status = excluded.Status,
                StatusDetail = excluded.StatusDetail,
                Progress = excluded.Progress,
                DownloadedBytes = excluded.DownloadedBytes,
                TotalBytes = excluded.TotalBytes,
                SpeedStr = excluded.SpeedStr,
                EtaStr = excluded.EtaStr,
                SizeStr = excluded.SizeStr,
                OutputPath = excluded.OutputPath,
                ThumbnailPath = excluded.ThumbnailPath,
                ErrorMessage = excluded.ErrorMessage,
                IsLive = excluded.IsLive,
                LiveDurationStr = excluded.LiveDurationStr,
                FinishedAt = excluded.FinishedAt,
                ScheduleType = excluded.ScheduleType,
                ScheduledTime = excluded.ScheduledTime,
                AudioLanguage = excluded.AudioLanguage,
                SubtitleTrack = excluded.SubtitleTrack,
                VideoResolution = excluded.VideoResolution,
                VideoFps = excluded.VideoFps,
                VideoCodec = excluded.VideoCodec,
                VideoDynamicRange = excluded.VideoDynamicRange,
                ContainerFormat = excluded.ContainerFormat,
                SpeedLimit = excluded.SpeedLimit,
                AudioCodec = excluded.AudioCodec,
                AudioBitrate = excluded.AudioBitrate,
                AudioSampleRate = excluded.AudioSampleRate,
                AudioBitDepth = excluded.AudioBitDepth,
                AudioChannels = excluded.AudioChannels,
                EmbedSubtitles = excluded.EmbedSubtitles,
                TargetFolder = excluded.TargetFolder,
                PlaylistId = excluded.PlaylistId,
                PlaylistIndex = excluded.PlaylistIndex,
                PlaylistFolder = excluded.PlaylistFolder,
                Attempts = excluded.Attempts,
                LastError = excluded.LastError,
                NamingTemplate = excluded.NamingTemplate;
        ";

        cmd.Parameters.AddWithValue("$Id", item.Id);
        cmd.Parameters.AddWithValue("$Url", item.Url ?? string.Empty);
        cmd.Parameters.AddWithValue("$TargetUrl", item.TargetUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$Title", item.Title ?? string.Empty);
        cmd.Parameters.AddWithValue("$CustomTitle", item.CustomTitle ?? string.Empty);
        cmd.Parameters.AddWithValue("$Quality", item.Quality ?? "1080p Full HD");
        cmd.Parameters.AddWithValue("$Codec", item.Codec ?? string.Empty);
        cmd.Parameters.AddWithValue("$Status", (int)item.Status);
        cmd.Parameters.AddWithValue("$StatusDetail", item.StatusDetail ?? string.Empty);
        cmd.Parameters.AddWithValue("$Progress", item.Progress);
        cmd.Parameters.AddWithValue("$DownloadedBytes", item.DownloadedBytes);
        cmd.Parameters.AddWithValue("$TotalBytes", item.TotalBytes);
        cmd.Parameters.AddWithValue("$SpeedStr", item.SpeedStr ?? string.Empty);
        cmd.Parameters.AddWithValue("$EtaStr", item.EtaStr ?? string.Empty);
        cmd.Parameters.AddWithValue("$SizeStr", item.SizeStr ?? "--");
        cmd.Parameters.AddWithValue("$OutputPath", item.OutputPath ?? string.Empty);
        cmd.Parameters.AddWithValue("$ThumbnailPath", item.ThumbnailPath ?? string.Empty);
        cmd.Parameters.AddWithValue("$ErrorMessage", item.ErrorMessage ?? string.Empty);
        cmd.Parameters.AddWithValue("$IsLive", item.IsLive ? 1 : 0);
        cmd.Parameters.AddWithValue("$LiveDurationStr", item.LiveDurationStr ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioLanguage", item.AudioLanguage ?? string.Empty);
        cmd.Parameters.AddWithValue("$SubtitleTrack", item.SubtitleTrack ?? string.Empty);
        cmd.Parameters.AddWithValue("$VideoResolution", item.VideoResolution ?? string.Empty);
        cmd.Parameters.AddWithValue("$VideoFps", item.VideoFps ?? string.Empty);
        cmd.Parameters.AddWithValue("$VideoCodec", item.VideoCodec ?? string.Empty);
        cmd.Parameters.AddWithValue("$VideoDynamicRange", item.VideoDynamicRange ?? string.Empty);
        cmd.Parameters.AddWithValue("$ContainerFormat", item.ContainerFormat ?? string.Empty);
        cmd.Parameters.AddWithValue("$SpeedLimit", item.SpeedLimit ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioCodec", item.AudioCodec ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioBitrate", item.AudioBitrate ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioSampleRate", item.AudioSampleRate ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioBitDepth", item.AudioBitDepth ?? string.Empty);
        cmd.Parameters.AddWithValue("$AudioChannels", item.AudioChannels ?? string.Empty);
        cmd.Parameters.AddWithValue("$EmbedSubtitles", item.EmbedSubtitles ? "1" : "0");
        cmd.Parameters.AddWithValue("$TargetFolder", item.TargetFolder ?? string.Empty);
        cmd.Parameters.AddWithValue("$PlaylistId", (object?)item.PlaylistId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$PlaylistIndex", item.PlaylistIndex);
        cmd.Parameters.AddWithValue("$PlaylistFolder", item.PlaylistFolder ?? string.Empty);
        cmd.Parameters.AddWithValue("$Attempts", item.Attempts);
        cmd.Parameters.AddWithValue("$LastError", item.LastError ?? string.Empty);
        cmd.Parameters.AddWithValue("$NamingTemplate", item.NamingTemplate ?? string.Empty);
        cmd.Parameters.AddWithValue("$CreatedAt", item.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$FinishedAt", item.FinishedAt.HasValue ? item.FinishedAt.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ScheduleType", (object?)item.ScheduleType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ScheduledTime", item.ScheduledTime.HasValue ? item.ScheduledTime.Value.ToString("o") : (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<DownloadItem>> GetItemsAsync(DateFilter filter = DateFilter.All, string? searchQuery = null, DateTime? specificDate = null, int limit = 20, int offset = 0, bool standaloneOnly = true)
    {
        var items = new List<DownloadItem>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var conditions = new List<string>();

        if (standaloneOnly)
        {
            conditions.Add("(PlaylistId IS NULL OR PlaylistId = '')");
        }

        if (specificDate.HasValue)
        {
            DateTime start = specificDate.Value.Date;
            DateTime end = start.AddDays(1);
            conditions.Add("CreatedAt >= $StartDate AND CreatedAt < $EndDate");
            cmd.Parameters.AddWithValue("$StartDate", start.ToString("o"));
            cmd.Parameters.AddWithValue("$EndDate", end.ToString("o"));
        }
        else if (filter != DateFilter.All)
        {
            var now = DateTime.Now;
            DateTime threshold = filter switch
            {
                DateFilter.Today => now.Date,
                DateFilter.Last7Days => now.Date.AddDays(-7),
                DateFilter.ThisMonth => new DateTime(now.Year, now.Month, 1),
                _ => DateTime.MinValue
            };
            conditions.Add("CreatedAt >= $Threshold");
            cmd.Parameters.AddWithValue("$Threshold", threshold.ToString("o"));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            conditions.Add("(Title LIKE $Query OR CustomTitle LIKE $Query OR Url LIKE $Query)");
            cmd.Parameters.AddWithValue("$Query", $"%{searchQuery.Trim()}%");
        }

        string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        string limitClause = limit > 0 ? $"LIMIT {limit} OFFSET {offset}" : string.Empty;
        cmd.CommandText = $"SELECT * FROM Downloads {whereClause} ORDER BY CreatedAt DESC {limitClause};";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapFromReader(reader));
        }

        return items;
    }

    public async Task<int> GetItemsCountAsync(DateFilter filter = DateFilter.All, string? searchQuery = null, DateTime? specificDate = null, bool standaloneOnly = true)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var conditions = new List<string>();

        if (standaloneOnly)
        {
            conditions.Add("(PlaylistId IS NULL OR PlaylistId = '')");
        }

        if (specificDate.HasValue)
        {
            DateTime start = specificDate.Value.Date;
            DateTime end = start.AddDays(1);
            conditions.Add("CreatedAt >= $StartDate AND CreatedAt < $EndDate");
            cmd.Parameters.AddWithValue("$StartDate", start.ToString("o"));
            cmd.Parameters.AddWithValue("$EndDate", end.ToString("o"));
        }
        else if (filter != DateFilter.All)
        {
            var now = DateTime.Now;
            DateTime threshold = filter switch
            {
                DateFilter.Today => now.Date,
                DateFilter.Last7Days => now.Date.AddDays(-7),
                DateFilter.ThisMonth => new DateTime(now.Year, now.Month, 1),
                _ => DateTime.MinValue
            };
            conditions.Add("CreatedAt >= $Threshold");
            cmd.Parameters.AddWithValue("$Threshold", threshold.ToString("o"));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            conditions.Add("(Title LIKE $Query OR CustomTitle LIKE $Query OR Url LIKE $Query)");
            cmd.Parameters.AddWithValue("$Query", $"%{searchQuery.Trim()}%");
        }

        string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        cmd.CommandText = $"SELECT COUNT(*) FROM Downloads {whereClause};";

        var res = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(res);
    }

    public async Task DeleteItemAsync(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Downloads WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearFinishedItemsAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Downloads WHERE Status = $FinishedStatus;";
        cmd.Parameters.AddWithValue("$FinishedStatus", (int)JobStatus.Finished);
        await cmd.ExecuteNonQueryAsync();

        using var cmdPl = connection.CreateCommand();
        cmdPl.CommandText = "DELETE FROM Playlists WHERE Status = $FinishedStatus;";
        cmdPl.Parameters.AddWithValue("$FinishedStatus", (int)JobStatus.Finished);
        await cmdPl.ExecuteNonQueryAsync();
    }

    private static DownloadItem MapFromReader(SqliteDataReader reader)
    {
        var item = new DownloadItem
        {
            Id = reader.GetString(reader.GetOrdinal("Id")),
            Url = reader.IsDBNull(reader.GetOrdinal("Url")) ? string.Empty : reader.GetString(reader.GetOrdinal("Url")),
            TargetUrl = reader.IsDBNull(reader.GetOrdinal("TargetUrl")) ? string.Empty : reader.GetString(reader.GetOrdinal("TargetUrl")),
            Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? string.Empty : reader.GetString(reader.GetOrdinal("Title")),
            CustomTitle = reader.IsDBNull(reader.GetOrdinal("CustomTitle")) ? string.Empty : reader.GetString(reader.GetOrdinal("CustomTitle")),
            Quality = reader.IsDBNull(reader.GetOrdinal("Quality")) ? "1080p Full HD" : reader.GetString(reader.GetOrdinal("Quality")),
            Codec = reader.IsDBNull(reader.GetOrdinal("Codec")) ? string.Empty : reader.GetString(reader.GetOrdinal("Codec")),
            Status = (JobStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            StatusDetail = reader.IsDBNull(reader.GetOrdinal("StatusDetail")) ? string.Empty : reader.GetString(reader.GetOrdinal("StatusDetail")),
            Progress = reader.IsDBNull(reader.GetOrdinal("Progress")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("Progress")),
            DownloadedBytes = reader.IsDBNull(reader.GetOrdinal("DownloadedBytes")) ? 0 : reader.GetInt64(reader.GetOrdinal("DownloadedBytes")),
            TotalBytes = reader.IsDBNull(reader.GetOrdinal("TotalBytes")) ? 0 : reader.GetInt64(reader.GetOrdinal("TotalBytes")),
            SpeedStr = reader.IsDBNull(reader.GetOrdinal("SpeedStr")) ? string.Empty : reader.GetString(reader.GetOrdinal("SpeedStr")),
            EtaStr = reader.IsDBNull(reader.GetOrdinal("EtaStr")) ? string.Empty : reader.GetString(reader.GetOrdinal("EtaStr")),
            SizeStr = reader.IsDBNull(reader.GetOrdinal("SizeStr")) ? "--" : reader.GetString(reader.GetOrdinal("SizeStr")),
            OutputPath = reader.IsDBNull(reader.GetOrdinal("OutputPath")) ? string.Empty : reader.GetString(reader.GetOrdinal("OutputPath")),
            TargetFolder = HasColumn(reader, "TargetFolder") && !reader.IsDBNull(reader.GetOrdinal("TargetFolder")) ? reader.GetString(reader.GetOrdinal("TargetFolder")) : string.Empty,
            ThumbnailPath = reader.IsDBNull(reader.GetOrdinal("ThumbnailPath")) ? string.Empty : reader.GetString(reader.GetOrdinal("ThumbnailPath")),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? string.Empty : reader.GetString(reader.GetOrdinal("ErrorMessage")),
            IsLive = reader.GetInt32(reader.GetOrdinal("IsLive")) == 1,
            LiveDurationStr = reader.IsDBNull(reader.GetOrdinal("LiveDurationStr")) ? string.Empty : reader.GetString(reader.GetOrdinal("LiveDurationStr")),
            ScheduleType = reader.IsDBNull(reader.GetOrdinal("ScheduleType")) ? null : reader.GetString(reader.GetOrdinal("ScheduleType")),
            AudioLanguage = HasColumn(reader, "AudioLanguage") && !reader.IsDBNull(reader.GetOrdinal("AudioLanguage")) ? reader.GetString(reader.GetOrdinal("AudioLanguage")) : string.Empty,
            SubtitleTrack = HasColumn(reader, "SubtitleTrack") && !reader.IsDBNull(reader.GetOrdinal("SubtitleTrack")) ? reader.GetString(reader.GetOrdinal("SubtitleTrack")) : string.Empty,
            VideoResolution = HasColumn(reader, "VideoResolution") && !reader.IsDBNull(reader.GetOrdinal("VideoResolution")) ? reader.GetString(reader.GetOrdinal("VideoResolution")) : string.Empty,
            VideoFps = HasColumn(reader, "VideoFps") && !reader.IsDBNull(reader.GetOrdinal("VideoFps")) ? reader.GetString(reader.GetOrdinal("VideoFps")) : string.Empty,
            VideoCodec = HasColumn(reader, "VideoCodec") && !reader.IsDBNull(reader.GetOrdinal("VideoCodec")) ? reader.GetString(reader.GetOrdinal("VideoCodec")) : string.Empty,
            VideoDynamicRange = HasColumn(reader, "VideoDynamicRange") && !reader.IsDBNull(reader.GetOrdinal("VideoDynamicRange")) ? reader.GetString(reader.GetOrdinal("VideoDynamicRange")) : string.Empty,
            ContainerFormat = HasColumn(reader, "ContainerFormat") && !reader.IsDBNull(reader.GetOrdinal("ContainerFormat")) ? reader.GetString(reader.GetOrdinal("ContainerFormat")) : string.Empty,
            SpeedLimit = HasColumn(reader, "SpeedLimit") && !reader.IsDBNull(reader.GetOrdinal("SpeedLimit")) ? reader.GetString(reader.GetOrdinal("SpeedLimit")) : string.Empty,
            AudioCodec = HasColumn(reader, "AudioCodec") && !reader.IsDBNull(reader.GetOrdinal("AudioCodec")) ? reader.GetString(reader.GetOrdinal("AudioCodec")) : string.Empty,
            AudioBitrate = HasColumn(reader, "AudioBitrate") && !reader.IsDBNull(reader.GetOrdinal("AudioBitrate")) ? reader.GetString(reader.GetOrdinal("AudioBitrate")) : string.Empty,
            AudioSampleRate = HasColumn(reader, "AudioSampleRate") && !reader.IsDBNull(reader.GetOrdinal("AudioSampleRate")) ? reader.GetString(reader.GetOrdinal("AudioSampleRate")) : string.Empty,
            AudioBitDepth = HasColumn(reader, "AudioBitDepth") && !reader.IsDBNull(reader.GetOrdinal("AudioBitDepth")) ? reader.GetString(reader.GetOrdinal("AudioBitDepth")) : string.Empty,
            AudioChannels = HasColumn(reader, "AudioChannels") && !reader.IsDBNull(reader.GetOrdinal("AudioChannels")) ? reader.GetString(reader.GetOrdinal("AudioChannels")) : string.Empty,
            EmbedSubtitles = !HasColumn(reader, "EmbedSubtitles") || reader.IsDBNull(reader.GetOrdinal("EmbedSubtitles")) || reader.GetString(reader.GetOrdinal("EmbedSubtitles")) != "0",
            PlaylistId = HasColumn(reader, "PlaylistId") && !reader.IsDBNull(reader.GetOrdinal("PlaylistId")) ? reader.GetString(reader.GetOrdinal("PlaylistId")) : null,
            PlaylistIndex = HasColumn(reader, "PlaylistIndex") && !reader.IsDBNull(reader.GetOrdinal("PlaylistIndex")) ? (int.TryParse(reader.GetString(reader.GetOrdinal("PlaylistIndex")), out var pIdx) ? pIdx : 0) : 0,
            PlaylistFolder = HasColumn(reader, "PlaylistFolder") && !reader.IsDBNull(reader.GetOrdinal("PlaylistFolder")) ? reader.GetString(reader.GetOrdinal("PlaylistFolder")) : string.Empty,
            Attempts = HasColumn(reader, "Attempts") && !reader.IsDBNull(reader.GetOrdinal("Attempts")) ? (int.TryParse(reader.GetString(reader.GetOrdinal("Attempts")), out var att) ? att : 0) : 0,
            LastError = HasColumn(reader, "LastError") && !reader.IsDBNull(reader.GetOrdinal("LastError")) ? reader.GetString(reader.GetOrdinal("LastError")) : string.Empty,
            NamingTemplate = HasColumn(reader, "NamingTemplate") && !reader.IsDBNull(reader.GetOrdinal("NamingTemplate")) ? reader.GetString(reader.GetOrdinal("NamingTemplate")) : string.Empty
        };

        if (DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var created))
            item.CreatedAt = created;

        if (!reader.IsDBNull(reader.GetOrdinal("FinishedAt")) && DateTime.TryParse(reader.GetString(reader.GetOrdinal("FinishedAt")), out var finished))
            item.FinishedAt = finished;

        if (!reader.IsDBNull(reader.GetOrdinal("ScheduledTime")) && DateTime.TryParse(reader.GetString(reader.GetOrdinal("ScheduledTime")), out var scheduled))
            item.ScheduledTime = scheduled;

        return item;
    }

    public async Task UpsertPlaylistAsync(PlaylistJob playlist)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Playlists (
                Id, Title, SourceUrl, TargetFolder, Status, TotalItems, CustomTitle, CreatedAt, FinishedAt, NamingTemplate, PreferredQuality
            ) VALUES (
                $Id, $Title, $SourceUrl, $TargetFolder, $Status, $TotalItems, $CustomTitle, $CreatedAt, $FinishedAt, $NamingTemplate, $PreferredQuality
            )
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                SourceUrl = excluded.SourceUrl,
                TargetFolder = excluded.TargetFolder,
                Status = excluded.Status,
                TotalItems = excluded.TotalItems,
                CustomTitle = excluded.CustomTitle,
                FinishedAt = excluded.FinishedAt,
                NamingTemplate = excluded.NamingTemplate,
                PreferredQuality = excluded.PreferredQuality;
        ";

        cmd.Parameters.AddWithValue("$Id", playlist.Id);
        cmd.Parameters.AddWithValue("$Title", playlist.Title ?? string.Empty);
        cmd.Parameters.AddWithValue("$SourceUrl", playlist.SourceUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$TargetFolder", playlist.TargetFolder ?? string.Empty);
        cmd.Parameters.AddWithValue("$Status", (int)playlist.Status);
        cmd.Parameters.AddWithValue("$TotalItems", playlist.TotalItems);
        cmd.Parameters.AddWithValue("$CustomTitle", playlist.CustomTitle ?? string.Empty);
        cmd.Parameters.AddWithValue("$CreatedAt", playlist.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$FinishedAt", playlist.FinishedAt.HasValue ? playlist.FinishedAt.Value.ToString("o") : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$NamingTemplate", playlist.NamingTemplate ?? string.Empty);
        cmd.Parameters.AddWithValue("$PreferredQuality", playlist.PreferredQuality ?? string.Empty);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PlaylistJob>> GetPlaylistsAsync(DateFilter filter = DateFilter.All, string? searchQuery = null, DateTime? specificDate = null)
    {
        var list = new List<PlaylistJob>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        var conditions = new List<string>();

        if (specificDate.HasValue)
        {
            DateTime start = specificDate.Value.Date;
            DateTime end = start.AddDays(1);
            conditions.Add("CreatedAt >= $StartDate AND CreatedAt < $EndDate");
            cmd.Parameters.AddWithValue("$StartDate", start.ToString("o"));
            cmd.Parameters.AddWithValue("$EndDate", end.ToString("o"));
        }
        else if (filter != DateFilter.All)
        {
            var now = DateTime.Now;
            DateTime threshold = filter switch
            {
                DateFilter.Today => now.Date,
                DateFilter.Last7Days => now.Date.AddDays(-7),
                DateFilter.ThisMonth => new DateTime(now.Year, now.Month, 1),
                _ => DateTime.MinValue
            };
            conditions.Add("CreatedAt >= $Threshold");
            cmd.Parameters.AddWithValue("$Threshold", threshold.ToString("o"));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            conditions.Add("(Title LIKE $Query OR CustomTitle LIKE $Query OR SourceUrl LIKE $Query)");
            cmd.Parameters.AddWithValue("$Query", $"%{searchQuery.Trim()}%");
        }

        string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;
        cmd.CommandText = $"SELECT * FROM Playlists {whereClause} ORDER BY CreatedAt DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var pl = new PlaylistJob
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? string.Empty : reader.GetString(reader.GetOrdinal("Title")),
                SourceUrl = reader.IsDBNull(reader.GetOrdinal("SourceUrl")) ? string.Empty : reader.GetString(reader.GetOrdinal("SourceUrl")),
                TargetFolder = reader.IsDBNull(reader.GetOrdinal("TargetFolder")) ? string.Empty : reader.GetString(reader.GetOrdinal("TargetFolder")),
                Status = (JobStatus)reader.GetInt32(reader.GetOrdinal("Status")),
                TotalItems = reader.IsDBNull(reader.GetOrdinal("TotalItems")) ? 0 : reader.GetInt32(reader.GetOrdinal("TotalItems")),
                CustomTitle = reader.IsDBNull(reader.GetOrdinal("CustomTitle")) ? string.Empty : reader.GetString(reader.GetOrdinal("CustomTitle")),
                NamingTemplate = HasColumn(reader, "NamingTemplate") && !reader.IsDBNull(reader.GetOrdinal("NamingTemplate")) ? reader.GetString(reader.GetOrdinal("NamingTemplate")) : string.Empty,
                PreferredQuality = HasColumn(reader, "PreferredQuality") && !reader.IsDBNull(reader.GetOrdinal("PreferredQuality")) ? reader.GetString(reader.GetOrdinal("PreferredQuality")) : string.Empty
            };

            if (DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var created))
                pl.CreatedAt = created;

            if (!reader.IsDBNull(reader.GetOrdinal("FinishedAt")) && DateTime.TryParse(reader.GetString(reader.GetOrdinal("FinishedAt")), out var fin))
                pl.FinishedAt = fin;

            list.Add(pl);
        }

        return list;
    }

    public async Task<PlaylistJob?> GetPlaylistBySourceUrlAsync(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return null;
        var all = await GetPlaylistsAsync();
        string key = DuplicateDetectionService.ExtractVideoKey(sourceUrl);
        return all.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.SourceUrl) &&
            (p.SourceUrl.Equals(sourceUrl, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(key) && DuplicateDetectionService.ExtractVideoKey(p.SourceUrl) == key)));
    }

    public async Task DeletePlaylistAsync(string id)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Playlists WHERE Id = $Id;";
        cmd.Parameters.AddWithValue("$Id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<DownloadItem>> GetPlaylistItemsAsync(string playlistId)
    {
        var items = new List<DownloadItem>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Downloads WHERE PlaylistId = $PlaylistId ORDER BY PlaylistIndex ASC, CreatedAt ASC;";
        cmd.Parameters.AddWithValue("$PlaylistId", playlistId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(MapFromReader(reader));
        }

        return items;
    }

    private static bool HasColumn(SqliteDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
