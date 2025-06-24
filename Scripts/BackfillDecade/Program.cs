using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using System.Globalization;
using System.Text.RegularExpressions;

class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration with user secrets
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Program>()
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddHttpClient("MusicBrainzClient", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "BlueNestKaraoke/1.0 (tstrand@strandcentral.com)");
        });
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(configuration);
        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // Get Spotify token
            var client = httpClientFactory.CreateClient("MusicBrainzClient");
            var clientId = configuration["Spotify:ClientId"];
            var clientSecret = configuration["Spotify:ClientSecret"];
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                throw new InvalidOperationException("Spotify ClientId or ClientSecret is missing in configuration.");
            }

            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret }
                })
            };

            var tokenResponse = await client.SendAsync(tokenRequest);
            tokenResponse.EnsureSuccessStatusCode();
            var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<SpotifyTokenResponse>(tokenJson);
            var token = tokenData?.AccessToken ?? throw new InvalidOperationException("Failed to obtain Spotify token.");

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
            client.DefaultRequestHeaders.Add("User-Agent", "BlueNestKaraoke/1.0 (tstrand@strandcentral.com)");

            // Get Last.fm API key and secret
            var lastFmApiKey = configuration["LastFM:ApiKey"];
            var lastFmApiSecret = configuration["LastFM:ApiSecret"];
            if (string.IsNullOrEmpty(lastFmApiKey) || string.IsNullOrEmpty(lastFmApiSecret))
            {
                logger.LogWarning("Last.fm API key or secret is missing in configuration. Genre and metadata fetching from Last.fm will be skipped.");
            }

            // Fetch all songs with Spotify IDs to update metadata
            var songs = await context.Songs
                .Where(s => s.SpotifyId != null)
                .ToListAsync();
            logger.LogInformation("Found {SongCount} songs with Spotify IDs for metadata updates.", songs.Count);

            int updatedGenreCount = 0;
            int updatedMusicBrainzIdCount = 0;
            int updatedPlaycountCount = 0;
            int updatedDanceabilityCount = 0;
            int updatedEnergyCount = 0;
            int updatedMoodCount = 0;
            int updatedValenceCount = 0;
            const int maxRetries = 3;
            const int delayMs = 100; // Delay between API calls (Spotify, Last.fm)
            const int musicBrainzDelayMs = 1000; // MusicBrainz requires 1 request per second

            foreach (var song in songs)
            {
                // Visual separator for each song
                logger.LogInformation("********************");
                logger.LogInformation("Processing song '{Title}' (ID: {SongId}, SpotifyID: {SpotifyId})", song.Title, song.Id, song.SpotifyId);
                logger.LogInformation("Initial state - Decade: {Decade}, Genre: {Genre}, MusicBrainzId: {MusicBrainzId}, Danceability: {Danceability}, Energy: {Energy}, Mood: {Mood}, Valence: {Valence}, LastFmPlaycount: {LastFmPlaycount}",
                    song.Decade ?? "null" as string, song.Genre ?? "null" as string, song.MusicBrainzId ?? "null" as string, song.Danceability ?? "null" as string, song.Energy ?? "null" as string, song.Mood ?? "null" as string, song.Valence?.ToString() ?? "null", song.LastFmPlaycount?.ToString() ?? "null");
                bool updated = false;

                // Retry logic for fetching Spotify track data
                HttpResponseMessage? trackResponse = null;
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        trackResponse = await client.GetAsync($"https://api.spotify.com/v1/tracks/{song.SpotifyId!}");
                        if (trackResponse.IsSuccessStatusCode)
                        {
                            break;
                        }
                        else
                        {
                            var errorText = await trackResponse.Content.ReadAsStringAsync();
                            logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch track data for SpotifyID: {SpotifyId}. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, song.SpotifyId, (int)trackResponse.StatusCode, errorText);
                            await Task.Delay(delayMs * (retry + 1)); // Exponential backoff
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching track data for SpotifyID: {SpotifyId}", retry + 1, maxRetries, song.SpotifyId);
                        await Task.Delay(delayMs * (retry + 1));
                    }
                }

                if (trackResponse != null && trackResponse.IsSuccessStatusCode)
                {
                    var trackJson = await trackResponse.Content.ReadAsStringAsync();
                    var trackDetails = JsonSerializer.Deserialize<SpotifyTrackDetails>(trackJson);
                    if (trackDetails != null)
                    {
                        string? trackArtist = null;
                        string trackName = song.Title ?? string.Empty; // Handle null Title to suppress CS8600 warning

                        // Get artist name for Last.fm and MusicBrainz queries
                        if (trackDetails.Artists?.Any() == true)
                        {
                            trackArtist = trackDetails.Artists[0].Name;
                        }

                        // Clean the track name and artist to remove karaoke references
                        string originalTrackName = CleanTrackName(trackName);
                        string originalTrackArtist = trackArtist != null ? Regex.Replace(trackArtist, @"\s*Karaoke\s*", "", RegexOptions.IgnoreCase).Trim() : trackArtist;

                        // Update the song's Title and Artist to the original values
                        bool titleUpdated = false;
                        bool artistUpdated = false;
                        if (!string.Equals(song.Title, originalTrackName, StringComparison.OrdinalIgnoreCase))
                        {
                            song.Title = originalTrackName;
                            titleUpdated = true;
                            logger.LogInformation("Updated Title to '{Title}' for song (ID: {SongId})", song.Title, song.Id);
                        }
                        if (originalTrackArtist != null && !string.Equals(song.Artist, originalTrackArtist, StringComparison.OrdinalIgnoreCase))
                        {
                            song.Artist = originalTrackArtist;
                            artistUpdated = true;
                            logger.LogInformation("Updated Artist to '{Artist}' for song (ID: {SongId})", song.Artist, song.Id);
                        }
                        updated |= titleUpdated || artistUpdated;

                        // Use the cleaned artist and track name for further queries
                        trackName = originalTrackName;
                        trackArtist = originalTrackArtist;

                        // Update genre (always update if new data is found)
                        string genre = "Unknown";

                        // Try Spotify: track's primary artist
                        if (trackDetails.Artists != null && trackDetails.Artists.Any() == true)
                        {
                            var artistId = trackDetails.Artists![0].Id;
                            HttpResponseMessage? artistResponse = null;
                            for (int retry = 0; retry < maxRetries; retry++)
                            {
                                try
                                {
                                    artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{artistId}");
                                    if (artistResponse.IsSuccessStatusCode)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        var errorText = await artistResponse.Content.ReadAsStringAsync();
                                        logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch artist data for track's primary artist (ID: {ArtistId}). Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, artistId, (int)artistResponse.StatusCode, errorText);
                                        await Task.Delay(delayMs * (retry + 1));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching artist data for track's primary artist (ID: {ArtistId})", retry + 1, maxRetries, artistId);
                                    await Task.Delay(delayMs * (retry + 1));
                                }
                            }

                            if (artistResponse != null && artistResponse.IsSuccessStatusCode)
                            {
                                var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                if (artistDetails?.Genres.Any() == true)
                                {
                                    logger.LogInformation("Spotify track artist (ID: {ArtistId}) genres: [{Genres}]", artistId, string.Join(", ", artistDetails.Genres));
                                    genre = SelectGenre(artistDetails.Genres, logger);
                                    logger.LogDebug("Selected genre '{Genre}' from track's primary artist for track '{TrackId}'", genre, song.SpotifyId);
                                }
                                else
                                {
                                    logger.LogInformation("Spotify track artist (ID: {ArtistId}) has no genres.", artistId);
                                }
                            }
                            else
                            {
                                logger.LogWarning("Failed to fetch artist data for track's primary artist (ID: {ArtistId}) after retries.", artistId);
                            }
                        }
                        else
                        {
                            logger.LogInformation("No artists found for track '{TrackId}'", song.SpotifyId);
                        }

                        // Try Spotify: album's artists
                        if (genre == "Unknown" && trackDetails.Album?.Id != null)
                        {
                            HttpResponseMessage? albumResponse = null;
                            for (int retry = 0; retry < maxRetries; retry++)
                            {
                                try
                                {
                                    albumResponse = await client.GetAsync($"https://api.spotify.com/v1/albums/{trackDetails.Album!.Id}");
                                    if (albumResponse.IsSuccessStatusCode)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        var errorText = await albumResponse.Content.ReadAsStringAsync();
                                        logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch album data for album '{AlbumId}'. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, trackDetails.Album!.Id, (int)albumResponse.StatusCode, errorText);
                                        await Task.Delay(delayMs * (retry + 1));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching album data for album '{AlbumId}'", retry + 1, maxRetries, trackDetails.Album!.Id);
                                    await Task.Delay(delayMs * (retry + 1));
                                }
                            }

                            if (albumResponse != null && albumResponse.IsSuccessStatusCode)
                            {
                                var albumJson = await albumResponse.Content.ReadAsStringAsync();
                                var albumDetails = JsonSerializer.Deserialize<SpotifyAlbumDetails>(albumJson);
                                if (albumDetails != null && albumDetails.Artists != null && albumDetails.Artists.Any() == true)
                                {
                                    foreach (var albumArtist in albumDetails.Artists)
                                    {
                                        HttpResponseMessage? artistResponse = null;
                                        for (int retry = 0; retry < maxRetries; retry++)
                                        {
                                            try
                                            {
                                                artistResponse = await client.GetAsync($"https://api.spotify.com/v1/artists/{albumArtist.Id}");
                                                if (artistResponse.IsSuccessStatusCode)
                                                {
                                                    break;
                                                }
                                                else
                                                {
                                                    var errorText = await artistResponse.Content.ReadAsStringAsync();
                                                    logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch artist data for album artist (ID: {ArtistId}). Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, albumArtist.Id, (int)artistResponse.StatusCode, errorText);
                                                    await Task.Delay(delayMs * (retry + 1));
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching artist data for album artist (ID: {ArtistId})", retry + 1, maxRetries, albumArtist.Id);
                                                await Task.Delay(delayMs * (retry + 1));
                                            }
                                        }

                                        if (artistResponse != null && artistResponse.IsSuccessStatusCode)
                                        {
                                            var artistJson = await artistResponse.Content.ReadAsStringAsync();
                                            var artistDetails = JsonSerializer.Deserialize<SpotifyArtistDetails>(artistJson);
                                            if (artistDetails?.Genres.Any() == true)
                                            {
                                                logger.LogInformation("Spotify album artist (ID: {ArtistId}) genres: [{Genres}]", albumArtist.Id, string.Join(", ", artistDetails.Genres));
                                                genre = SelectGenre(artistDetails.Genres, logger);
                                                logger.LogDebug("Selected genre '{Genre}' from album's artist '{ArtistId}' for track '{TrackId}'", genre, albumArtist.Id, song.SpotifyId);
                                                break;
                                            }
                                            else
                                            {
                                                logger.LogInformation("Spotify album artist (ID: {ArtistId}) has no genres.", albumArtist.Id);
                                            }
                                        }
                                        else
                                        {
                                            logger.LogWarning("Failed to fetch artist data for album artist (ID: {ArtistId}) after retries.", albumArtist.Id);
                                        }

                                        await Task.Delay(delayMs); // Rate limiting
                                    }
                                }
                                else
                                {
                                    logger.LogInformation("No artists found for album '{AlbumId}' for track '{TrackId}'", trackDetails.Album!.Id, song.SpotifyId);
                                }
                            }
                            else
                            {
                                logger.LogWarning("Failed to fetch album data for album '{AlbumId}' after retries.", trackDetails.Album!.Id);
                            }
                        }
                        else if (genre == "Unknown")
                        {
                            logger.LogInformation("No album data available to fetch genres for track '{TrackId}'", song.SpotifyId);
                        }

                        // If Spotify didn't provide a genre, try Last.fm
                        string? mbidFromLastFm = null;
                        string? originalTrackArtistFromLastFm = null;
                        string? originalTrackNameFromLastFm = null;
                        if (!string.IsNullOrEmpty(lastFmApiKey) && trackArtist != null)
                        {
                            logger.LogInformation("Falling back to Last.fm for genre data for original track '{TrackName}' by '{Artist}'", trackName, trackArtist);
                            HttpResponseMessage? lastFmResponse = null;
                            for (int retry = 0; retry < maxRetries; retry++)
                            {
                                try
                                {
                                    var lastFmUrl = $"http://ws.audioscrobbler.com/2.0/?method=track.getInfo&api_key={lastFmApiKey}&artist={Uri.EscapeDataString(trackArtist)}&track={Uri.EscapeDataString(trackName)}&format=json";
                                    lastFmResponse = await client.GetAsync(lastFmUrl);
                                    if (lastFmResponse.IsSuccessStatusCode)
                                    {
                                        break;
                                    }
                                    else
                                    {
                                        var errorText = await lastFmResponse.Content.ReadAsStringAsync();
                                        logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch Last.fm data for track '{TrackName}' by '{Artist}'. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, trackName, trackArtist, (int)lastFmResponse.StatusCode, errorText);
                                        await Task.Delay(delayMs * (retry + 1));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching Last.fm data for track '{TrackName}' by '{Artist}'", retry + 1, maxRetries, trackName, trackArtist);
                                    await Task.Delay(delayMs * (retry + 1));
                                }
                            }

                            if (lastFmResponse != null && lastFmResponse.IsSuccessStatusCode)
                            {
                                var lastFmJson = await lastFmResponse.Content.ReadAsStringAsync();
                                LastFmTrackResponse? lastFmData = null;
                                try
                                {
                                    lastFmData = JsonSerializer.Deserialize<LastFmTrackResponse>(lastFmJson);
                                }
                                catch (Exception ex)
                                {
                                    logger.LogError(ex, "Failed to deserialize Last.fm response for track '{TrackName}' by '{Artist}': {Response}", trackName, trackArtist, lastFmJson);
                                    continue; // Skip to the next song
                                }

                                if (lastFmData?.Track?.TopTags?.Tag?.Any() == true && genre == "Unknown")
                                {
                                    var tags = lastFmData.Track.TopTags.Tag.Select(t => t.Name).ToList();
                                    logger.LogInformation("Last.fm tags for track '{TrackName}' by '{Artist}': [{Tags}]", trackName, trackArtist, string.Join(", ", tags));
                                    genre = SelectGenre(tags, logger);
                                    logger.LogDebug("Selected genre '{Genre}' from Last.fm tags for track '{TrackName}' by '{Artist}'", genre, trackName, trackArtist);
                                }
                                else if (genre == "Unknown")
                                {
                                    logger.LogInformation("Last.fm returned no tags for track '{TrackName}' by '{Artist}'", trackName, trackArtist);
                                }

                                // Fetch MusicBrainz ID and playcount from Last.fm (always update)
                                if (lastFmData?.Track != null)
                                {
                                    if (!string.IsNullOrEmpty(lastFmData.Track.Mbid))
                                    {
                                        mbidFromLastFm = lastFmData.Track.Mbid;
                                        song.MusicBrainzId = mbidFromLastFm;
                                        updatedMusicBrainzIdCount++;
                                        updated = true;
                                        logger.LogInformation("Updated MusicBrainzId to '{MusicBrainzId}' for song '{Title}' (ID: {SongId}) via Last.fm", song.MusicBrainzId, song.Title, song.Id);
                                    }
                                    else
                                    {
                                        logger.LogInformation("Last.fm response for track '{TrackName}' by '{Artist}' does not include an MBID.", trackName, trackArtist);
                                    }

                                    if (lastFmData.Track.Playcount.HasValue)
                                    {
                                        song.LastFmPlaycount = lastFmData.Track.Playcount.Value;
                                        updatedPlaycountCount++;
                                        updated = true;
                                        logger.LogInformation("Updated LastFmPlaycount to {LastFmPlaycount} for song '{Title}' (ID: {SongId})", song.LastFmPlaycount, song.Title, song.Id);
                                    }
                                }

                                // Store the original artist and track name from Last.fm
                                if (lastFmData?.Track?.Artist != null)
                                {
                                    originalTrackArtistFromLastFm = lastFmData.Track.Artist.Name;
                                    originalTrackNameFromLastFm = lastFmData.Track.Name;
                                    logger.LogDebug("Last.fm provided original artist '{OriginalArtist}' and track '{OriginalTrack}' for track '{TrackName}' by '{Artist}'", originalTrackArtistFromLastFm, originalTrackNameFromLastFm, trackName, trackArtist);

                                    // Update Artist and Title if Last.fm provides better original values
                                    if (originalTrackArtistFromLastFm != null && !string.Equals(song.Artist, originalTrackArtistFromLastFm, StringComparison.OrdinalIgnoreCase))
                                    {
                                        song.Artist = originalTrackArtistFromLastFm;
                                        artistUpdated = true;
                                        logger.LogInformation("Updated Artist to '{Artist}' for song (ID: {SongId}) based on Last.fm data", song.Artist, song.Id);
                                    }
                                    if (originalTrackNameFromLastFm != null && !string.Equals(song.Title, originalTrackNameFromLastFm, StringComparison.OrdinalIgnoreCase))
                                    {
                                        song.Title = originalTrackNameFromLastFm;
                                        titleUpdated = true;
                                        logger.LogInformation("Updated Title to '{Title}' for song (ID: {SongId}) based on Last.fm data", song.Title, song.Id);
                                    }
                                    updated |= artistUpdated || titleUpdated;

                                    // Update trackArtist and trackName for further queries
                                    trackArtist = originalTrackArtistFromLastFm;
                                    trackName = originalTrackNameFromLastFm;
                                }
                            }
                            else
                            {
                                logger.LogWarning("Failed to fetch Last.fm data for track '{TrackName}' by '{Artist}' after retries.", trackName, trackArtist);
                            }
                        }

                        if (genre != "Unknown")
                        {
                            song.Genre = genre;
                            updatedGenreCount++;
                            updated = true;
                            logger.LogInformation("Updated Genre to '{Genre}' for song '{Title}' (ID: {SongId})", song.Genre, song.Title, song.Id);
                        }
                        else
                        {
                            logger.LogInformation("No genre found for song '{Title}' (ID: {SongId}) from Spotify or Last.fm.", song.Title, song.Id);
                        }

                        // Fetch Valence from Spotify
                        int? valence = await FetchValenceFromSpotify(client, song.SpotifyId!, logger, maxRetries, delayMs); // song.SpotifyId is guaranteed non-null due to Where clause
                        if (valence.HasValue && song.Valence != valence)
                        {
                            song.Valence = valence;
                            updatedValenceCount++;
                            updated = true;
                            logger.LogInformation("Updated Valence to {Valence} for song '{Title}' (ID: {SongId}) from Spotify", song.Valence, song.Title, song.Id);
                        }

                        // Fetch audio features from AcousticBrainz if MusicBrainzId is available
                        if (!string.IsNullOrEmpty(song.MusicBrainzId))
                        {
                            // First attempt: use the track's MBID
                            var (danceabilityUpdated, energyUpdated, moodUpdated) = await FetchAudioFeatures(client, song, song.MusicBrainzId, trackName, trackArtist!, "track", song.Genre, logger, maxRetries, musicBrainzDelayMs);
                            if (danceabilityUpdated) updatedDanceabilityCount++;
                            if (energyUpdated) updatedEnergyCount++;
                            if (moodUpdated) updatedMoodCount++;
                            updated |= danceabilityUpdated || energyUpdated || moodUpdated;

                            // Second attempt: if no danceability, try the original track's MBID
                            if (!danceabilityUpdated && originalTrackArtistFromLastFm != null && originalTrackNameFromLastFm != null &&
                                (!string.Equals(originalTrackNameFromLastFm, trackName, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals(originalTrackArtistFromLastFm, trackArtist, StringComparison.OrdinalIgnoreCase)))
                            {
                                logger.LogInformation("Attempting to fetch audio features for original track '{OriginalTrackName}' by '{OriginalArtist}'", originalTrackNameFromLastFm, originalTrackArtistFromLastFm);
                                string? originalMbid = await FetchMusicBrainzId(client, originalTrackArtistFromLastFm, originalTrackNameFromLastFm, logger, maxRetries, musicBrainzDelayMs);
                                if (!string.IsNullOrEmpty(originalMbid))
                                {
                                    var (originalDanceabilityUpdated, originalEnergyUpdated, originalMoodUpdated) = await FetchAudioFeatures(client, song, originalMbid, originalTrackNameFromLastFm, originalTrackArtistFromLastFm, "original track", song.Genre, logger, maxRetries, musicBrainzDelayMs);
                                    if (originalDanceabilityUpdated) updatedDanceabilityCount++;
                                    if (originalEnergyUpdated) updatedEnergyCount++;
                                    if (originalMoodUpdated) updatedMoodCount++;
                                    updated |= originalDanceabilityUpdated || originalEnergyUpdated || originalMoodUpdated;
                                }
                                else
                                {
                                    logger.LogWarning("Could not find MusicBrainz ID for original track '{OriginalTrackName}' by '{OriginalArtist}'", originalTrackNameFromLastFm, originalTrackArtistFromLastFm);
                                }
                            }
                        }
                        else
                        {
                            logger.LogInformation("Skipping AcousticBrainz lookup for song '{Title}' (ID: {SongId}) because MusicBrainzId is null.", song.Title, song.Id);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Failed to deserialize track data for SpotifyID: {SpotifyId}", song.SpotifyId);
                    }
                }
                else
                {
                    logger.LogWarning("Failed to fetch track data for SpotifyID: {SpotifyId} after retries.", song.SpotifyId);
                }

                if (updated)
                {
                    await context.SaveChangesAsync();
                }

                await Task.Delay(delayMs); // Rate limiting between songs
            }

            logger.LogInformation("Backfill complete. Updated {UpdatedGenreCount} songs with genres, {UpdatedMusicBrainzIdCount} songs with MusicBrainz IDs, {UpdatedPlaycountCount} songs with Last.fm playcounts, {UpdatedDanceabilityCount} songs with danceability, {UpdatedEnergyCount} songs with energy, {UpdatedMoodCount} songs with mood, and {UpdatedValenceCount} songs with valence.",
                updatedGenreCount, updatedMusicBrainzIdCount, updatedPlaycountCount, updatedDanceabilityCount, updatedEnergyCount, updatedMoodCount, updatedValenceCount);
            Console.WriteLine($"Backfill complete. Updated {updatedGenreCount} songs with genres, {updatedMusicBrainzIdCount} songs with MusicBrainz IDs, {updatedPlaycountCount} songs with Last.fm playcounts, {updatedDanceabilityCount} songs with danceability, {updatedEnergyCount} songs with energy, {updatedMoodCount} songs with mood, and {updatedValenceCount} songs with valence.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during backfill process");
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // Helper method to capitalize the first letter of each word in a genre
    private static string CapitalizeGenre(string genre)
    {
        if (string.IsNullOrEmpty(genre) || genre == "Unknown")
        {
            return genre;
        }

        // Use TextInfo.ToTitleCase to capitalize the first letter of each word
        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(genre.ToLower());
    }

    // Helper method to detect year-like genres (e.g., "90's", "1990s", "80s")
    private static bool IsYearLikeGenre(string genre)
    {
        if (string.IsNullOrEmpty(genre))
            return false;

        // Match patterns like "90's", "1990s", "80s", "2000s", etc.
        return Regex.IsMatch(genre, @"^(?:\d{2,4}s|\d{2}'s)$", RegexOptions.IgnoreCase);
    }

    // Helper method to select a genre, skipping year-like genres
    private static string SelectGenre(List<string> genres, ILogger logger)
    {
        foreach (var genre in genres)
        {
            if (!IsYearLikeGenre(genre))
            {
                return CapitalizeGenre(genre);
            }
            logger.LogDebug("Skipping year-like genre '{Genre}'", genre);
        }
        logger.LogWarning("No non-year-like genres found in list: [{Genres}]", string.Join(", ", genres));
        return "Unknown";
    }

    // Helper method to clean track names by removing karaoke indicators
    private static string CleanTrackName(string trackName)
    {
        // Remove common karaoke indicators like "(Karaoke Version)", "(Karaoke)", etc.
        return Regex.Replace(trackName, @"\s*\(Karaoke(?: Version)?\)\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    // Helper method to fetch MusicBrainz ID using the MusicBrainz API
    private static async Task<string?> FetchMusicBrainzId(HttpClient client, string artist, string track, ILogger logger, int maxRetries, int delayMs)
    {
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var query = Uri.EscapeDataString($"artist:\"{artist}\" AND recording:\"{track}\"");
                var url = $"https://musicbrainz.org/ws/2/recording?query={query}&fmt=json";
                logger.LogDebug("MusicBrainz API query: {Url}", url);
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var searchResult = JsonSerializer.Deserialize<MusicBrainzSearchResponse>(json);
                    if (searchResult?.Recordings?.Any() == true)
                    {
                        var bestMatch = searchResult.Recordings
                            .Where(r => r.Score >= 80) // Lowered threshold to capture more matches
                            .OrderByDescending(r => r.Score)
                            .FirstOrDefault();
                        if (bestMatch != null)
                        {
                            logger.LogDebug("Found MusicBrainz ID '{Mbid}' for track '{Track}' by '{Artist}' with score {Score}", bestMatch.Id, track, artist, bestMatch.Score);
                            return bestMatch.Id;
                        }
                        else
                        {
                            logger.LogDebug("No MusicBrainz matches with score >= 80 for track '{Track}' by '{Artist}': {Response}", track, artist, json);
                        }
                    }
                    else
                    {
                        logger.LogDebug("No MusicBrainz recordings found for track '{Track}' by '{Artist}': {Response}", track, artist, json);
                    }
                    break; // No need to retry if the response was successful but no match was found
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch MusicBrainz data for track '{Track}' by '{Artist}'. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, track, artist, (int)response.StatusCode, errorText);
                    await Task.Delay(delayMs * (retry + 1));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching MusicBrainz data for track '{Track}' by '{Artist}'", retry + 1, maxRetries, track, artist);
                await Task.Delay(delayMs * (retry + 1));
            }
        }
        return null;
    }

    // Helper method to fetch audio features from AcousticBrainz
    private static async Task<(bool danceabilityUpdated, bool energyUpdated, bool moodUpdated)> FetchAudioFeatures(HttpClient client, Song song, string mbid, string trackName, string artist, string source, string? genre, ILogger logger, int maxRetries, int delayMs)
    {
        bool danceabilityUpdated = false;
        bool energyUpdated = false;
        bool moodUpdated = false;

        logger.LogInformation("Fetching audio features from AcousticBrainz for {Source} '{TrackName}' by '{Artist}' with MusicBrainzId '{Mbid}'", source, trackName, artist, mbid);
        HttpResponseMessage? abResponse = null;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                abResponse = await client.GetAsync($"https://acousticbrainz.org/api/v1/{mbid}/high-level");
                if (abResponse.IsSuccessStatusCode)
                {
                    break;
                }
                else
                {
                    var errorText = await abResponse.Content.ReadAsStringAsync();
                    logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch AcousticBrainz data for MusicBrainzId '{Mbid}'. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, mbid, (int)abResponse.StatusCode, errorText);
                    await Task.Delay(delayMs * (retry + 1));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching AcousticBrainz data for MusicBrainzId '{Mbid}'", retry + 1, maxRetries, mbid);
                await Task.Delay(delayMs * (retry + 1));
            }
        }

        if (abResponse != null && abResponse.IsSuccessStatusCode)
        {
            var abJson = await abResponse.Content.ReadAsStringAsync();
            var abData = JsonSerializer.Deserialize<AcousticBrainzHighLevelResponse>(abJson);
            if (abData?.HighLevel != null)
            {
                // Update Danceability
                if (abData.HighLevel.Rhythm?.Danceability != null)
                {
                    song.Danceability = abData.HighLevel.Rhythm.Danceability;
                    danceabilityUpdated = true;
                    logger.LogInformation("Updated Danceability to '{Danceability}' for song '{Title}' (ID: {SongId}) from {Source}", song.Danceability, song.Title, song.Id, source);
                }

                // Update Energy with valence weighting
                if (abData.HighLevel.MoodAggressive?.Probability != null && abData.HighLevel.MoodRelaxed?.Probability != null)
                {
                    float aggressiveProb = abData.HighLevel.MoodAggressive.Probability ?? 0f;
                    float relaxedProb = abData.HighLevel.MoodRelaxed.Probability ?? 0f;

                    // Apply valence weighting (valence is 0-100 in the database, convert to 0.0-1.0)
                    float valenceWeight = song.Valence.HasValue ? (song.Valence.Value / 100f) - 0.5f : EstimateValenceWeightFromGenre(genre); // Normalize to -0.5 to 0.5
                    float adjustedAggressiveProb = aggressiveProb + (valenceWeight * 0.2f); // Boost aggressive if valence is high
                    float adjustedRelaxedProb = relaxedProb - (valenceWeight * 0.2f); // Reduce relaxed if valence is high
                    adjustedAggressiveProb = Math.Clamp(adjustedAggressiveProb, 0f, 1f);
                    adjustedRelaxedProb = Math.Clamp(adjustedRelaxedProb, 0f, 1f);

                    // Find the dominant probability
                    var energies = new (string Energy, float Probability, string Label)[]
                    {
                        ("Aggressive", adjustedAggressiveProb, adjustedAggressiveProb > 0.8 ? "Very Aggressive" : "Aggressive"),
                        ("Relaxed", adjustedRelaxedProb, adjustedRelaxedProb > 0.8 ? "Very Calm" : "Calm")
                    };

                    var dominantEnergy = energies.OrderByDescending(e => e.Probability).First();
                    if (dominantEnergy.Probability > 0.7) // Adjusted threshold for stronger confidence
                    {
                        song.Energy = dominantEnergy.Label;
                    }
                    else
                    {
                        song.Energy = "Neutral";
                    }

                    energyUpdated = true;
                    logger.LogInformation("Updated Energy to '{Energy}' for song '{Title}' (ID: {SongId}) from {Source} (Valence: {Valence}, Aggressive: {AggressiveProb}, Adjusted: {AdjustedAggressive}, Relaxed: {RelaxedProb}, Adjusted: {AdjustedRelaxed})",
                        song.Energy, song.Title, song.Id, source, song.Valence?.ToString() ?? "N/A", aggressiveProb, adjustedAggressiveProb, relaxedProb, adjustedRelaxedProb);
                }

                // Update Mood with valence weighting
                if (abData.HighLevel.MoodHappy?.Probability != null && abData.HighLevel.MoodSad?.Probability != null && abData.HighLevel.MoodParty?.Probability != null && abData.HighLevel.MoodRelaxed?.Probability != null)
                {
                    float happyProb = abData.HighLevel.MoodHappy.Probability ?? 0f;
                    float sadProb = abData.HighLevel.MoodSad.Probability ?? 0f;
                    float partyProb = abData.HighLevel.MoodParty.Probability ?? 0f;
                    float relaxedProb = abData.HighLevel.MoodRelaxed.Probability ?? 0f;

                    // Apply valence weighting (valence is 0-100 in the database, convert to 0.0-1.0)
                    float valenceWeight = song.Valence.HasValue ? (song.Valence.Value / 100f) - 0.5f : EstimateValenceWeightFromGenre(genre); // Normalize to -0.5 to 0.5
                    float adjustedHappyProb = happyProb + (valenceWeight * 0.1f); // Boost happy if valence is high
                    float adjustedSadProb = sadProb - (valenceWeight * 0.1f); // Reduce sad if valence is high
                    float adjustedPartyProb = partyProb + (valenceWeight * 0.15f); // Boost party if valence is high
                    float adjustedRelaxedProb = relaxedProb - (valenceWeight * 0.15f); // Reduce relaxed if valence is high
                    adjustedHappyProb = Math.Clamp(adjustedHappyProb, 0f, 1f);
                    adjustedSadProb = Math.Clamp(adjustedSadProb, 0f, 1f);
                    adjustedPartyProb = Math.Clamp(adjustedPartyProb, 0f, 1f);
                    adjustedRelaxedProb = Math.Clamp(adjustedRelaxedProb, 0f, 1f);

                    // Find the highest probability
                    var moods = new (string Mood, float Probability, string Label)[]
                    {
                        ("Happy", adjustedHappyProb, adjustedHappyProb > 0.8 ? "Very Happy" : "Happy"),
                        ("Sad", adjustedSadProb, adjustedSadProb > 0.8 ? "Very Sad" : "Sad"),
                        ("Party", adjustedPartyProb, "Party"),
                        ("Relaxed", adjustedRelaxedProb, "Relaxed")
                    };

                    var dominantMood = moods.OrderByDescending(m => m.Probability).First();
                    if (dominantMood.Probability > 0.7) // Adjusted threshold for stronger confidence
                    {
                        song.Mood = dominantMood.Label;
                    }
                    else
                    {
                        song.Mood = "Neutral";
                    }

                    moodUpdated = true;
                    logger.LogInformation("Updated Mood to '{Mood}' for song '{Title}' (ID: {SongId}) from {Source} (Valence: {Valence}, Happy: {HappyProb}, Adjusted: {AdjustedHappy}, Sad: {SadProb}, Adjusted: {AdjustedSad}, Party: {PartyProb}, Adjusted: {AdjustedParty}, Relaxed: {RelaxedProb}, Adjusted: {AdjustedRelaxed})",
                        song.Mood, song.Title, song.Id, source, song.Valence?.ToString() ?? "N/A", happyProb, adjustedHappyProb, sadProb, adjustedSadProb, partyProb, adjustedPartyProb, relaxedProb, adjustedRelaxedProb);
                }

                if (!danceabilityUpdated)
                {
                    logger.LogDebug("AcousticBrainz response for MusicBrainzId '{Mbid}' lacks rhythm data: {Response}", mbid, abJson);
                }
            }
            else
            {
                logger.LogInformation("AcousticBrainz returned no high-level data for MusicBrainzId '{Mbid}': {Response}", mbid, abJson);
            }
        }
        else
        {
            logger.LogWarning("Failed to fetch AcousticBrainz data for MusicBrainzId '{Mbid}' after retries.", mbid);
        }

        return (danceabilityUpdated, energyUpdated, moodUpdated);
    }

    // Helper method to fetch Valence from Spotify Audio Features API
    private static async Task<int?> FetchValenceFromSpotify(HttpClient client, string spotifyId, ILogger logger, int maxRetries, int delayMs)
    {
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var url = $"https://api.spotify.com/v1/audio-features/{spotifyId}";
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var audioFeatures = JsonSerializer.Deserialize<SpotifyAudioFeaturesResponse>(json);
                    if (audioFeatures?.Valence != null)
                    {
                        // Convert valence (0.0-1.0) to integer (0-100)
                        int valence = (int)(audioFeatures.Valence.Value * 100);
                        logger.LogDebug("Fetched Valence {Valence} from Spotify for SpotifyID '{SpotifyId}'", valence, spotifyId);
                        return valence;
                    }
                    else
                    {
                        logger.LogDebug("Spotify audio features response for SpotifyID '{SpotifyId}' lacks valence data: {Response}", spotifyId, json);
                    }
                    break; // No need to retry if the response was successful but no valence was found
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Attempt {Attempt}/{MaxRetries}: Failed to fetch Spotify audio features for SpotifyID '{SpotifyId}'. Status: {StatusCode}, Error: {ErrorText}", retry + 1, maxRetries, spotifyId, (int)response.StatusCode, errorText);
                    await Task.Delay(delayMs * (retry + 1));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries}: Exception fetching Spotify audio features for SpotifyID '{SpotifyId}'", retry + 1, maxRetries, spotifyId);
                await Task.Delay(delayMs * (retry + 1));
            }
        }
        return null;
    }

    // Helper method to estimate valence weight based on genre
    private static float EstimateValenceWeightFromGenre(string? genre)
    {
        if (string.IsNullOrEmpty(genre) || genre.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return 0f;

        // Genres typically associated with higher valence (bias toward Happy/Party)
        string[] highValenceGenres = { "pop", "dance", "disco", "rock", "punk", "hip hop", "electronic" };
        // Genres typically associated with lower valence (bias toward Sad/Relaxed)
        string[] lowValenceGenres = { "folk", "bluegrass", "classical", "jazz", "blues", "ballad" };

        genre = genre.ToLowerInvariant();
        if (highValenceGenres.Any(g => genre.Contains(g)))
            return 0.5f; // Bias toward Happy/Party/Aggressive
        if (lowValenceGenres.Any(g => genre.Contains(g)))
            return -0.5f; // Bias toward Sad/Relaxed/Calm
        return 0f; // Neutral for unknown or ambiguous genres
    }

    public class SpotifyTrackDetails
    {
        [JsonPropertyName("popularity")]
        public int Popularity { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("album")]
        public SpotifyAlbum? Album { get; set; }

        [JsonPropertyName("artists")]
        public List<SpotifyArtist>? Artists { get; set; }
    }

    public class SpotifyAlbum
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
    }

    public class SpotifyAlbumDetails
    {
        [JsonPropertyName("artists")]
        public List<SpotifyArtist>? Artists { get; set; }
    }

    public class SpotifyArtist
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }
        [JsonPropertyName("name")]
        public required string Name { get; set; }
    }

    public class SpotifyArtistDetails
    {
        [JsonPropertyName("genres")]
        public required List<string> Genres { get; set; }
    }

    public class SpotifyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; set; }
    }

    public class SpotifyAudioFeaturesResponse
    {
        [JsonPropertyName("valence")]
        public float? Valence { get; set; }
    }

    public class LastFmTrackResponse
    {
        [JsonPropertyName("track")]
        public LastFmTrack? Track { get; set; }
    }

    public class LastFmTrack
    {
        [JsonPropertyName("mbid")]
        public string? Mbid { get; set; }

        [JsonPropertyName("playcount")]
        public string? PlaycountRaw { get; set; } // Store the raw string value

        [JsonIgnore]
        public int? Playcount => int.TryParse(PlaycountRaw, out int result) ? result : null; // Parse to int?

        [JsonPropertyName("name")]
        public string? Name { get; set; } // Track name for original track lookup

        [JsonPropertyName("artist")]
        public LastFmArtist? Artist { get; set; } // Artist for original track lookup

        [JsonPropertyName("toptags")]
        public LastFmTopTags? TopTags { get; set; }
    }

    public class LastFmArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class LastFmTopTags
    {
        [JsonPropertyName("tag")]
        public List<LastFmTag>? Tag { get; set; }
    }

    public class LastFmTag
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class AcousticBrainzHighLevelResponse
    {
        [JsonPropertyName("highlevel")]
        public AcousticBrainzHighLevel? HighLevel { get; set; }
    }

    public class AcousticBrainzHighLevel
    {
        [JsonPropertyName("rhythm")]
        public AcousticBrainzRhythm? Rhythm { get; set; }

        [JsonPropertyName("mood_aggressive")]
        public AcousticBrainzMood? MoodAggressive { get; set; }

        [JsonPropertyName("mood_happy")]
        public AcousticBrainzMood? MoodHappy { get; set; }

        [JsonPropertyName("mood_sad")]
        public AcousticBrainzMood? MoodSad { get; set; }

        [JsonPropertyName("mood_relaxed")]
        public AcousticBrainzMood? MoodRelaxed { get; set; }

        [JsonPropertyName("mood_party")]
        public AcousticBrainzMood? MoodParty { get; set; }
    }

    public class AcousticBrainzRhythm
    {
        [JsonPropertyName("danceability")]
        public string? Danceability { get; set; }
    }

    public class AcousticBrainzMood
    {
        [JsonPropertyName("probability")]
        public float? Probability { get; set; }
    }

    public class MusicBrainzSearchResponse
    {
        [JsonPropertyName("recordings")]
        public List<MusicBrainzRecording>? Recordings { get; set; }
    }

    public class MusicBrainzRecording
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("score")]
        public int Score { get; set; }
    }
}