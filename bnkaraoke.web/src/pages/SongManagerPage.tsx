import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./SongManagerPage.css";

interface PendingSong {
  id: number;
  title: string;
  artist: string;
  genre: string;
  status: string;
  requestDate: string;
  spotifyId: string | null;
  firstName: string;
  lastName: string;
}

interface SongUpdate {
  Id: number;
  Title: string;
  Artist: string;
  Genre: string | null;
  Decade: string | null;
  Bpm: number | null;
  Danceability: string | null;
  Energy: string | null;
  Mood: string | null;
  Popularity: number | null;
  SpotifyId: string | null;
  YouTubeUrl: string | null;
  Status: string;
  MusicBrainzId: string | null;
  LastFmPlaycount: number | null;
  Valence: number | null;
}

interface YouTubeVideo {
  videoId: string;
  title: string;
  url: string;
  channelTitle: string;
  duration: string;
  uploadDate: string;
  viewCount: number;
}

interface KaraokeChannel {
  Id: number;
  ChannelName: string;
  ChannelId: string | null;
  SortOrder: number;
  IsActive: boolean;
}

const SongManagerPage: React.FC = () => {
  console.log("SongManagerPage component initializing");
  const navigate = useNavigate();
  const [pendingSongs, setPendingSongs] = useState<PendingSong[]>([]);
  const [manageableSongs, setManageableSongs] = useState<SongUpdate[]>([]);
  const [youtubeResults, setYoutubeResults] = useState<YouTubeVideo[]>([]);
  const [selectedSongId, setSelectedSongId] = useState<number | null>(null);
  const [manualLinks, setManualLinks] = useState<{ [key: number]: string }>({});
  const [showManualInput, setShowManualInput] = useState<{ [key: number]: boolean }>({});
  const [selectedVideo, setSelectedVideo] = useState<{ videoId: string; url: string } | null>(null);
  const [isVideoEmbeddable, setIsVideoEmbeddable] = useState<boolean>(true);
  const [showYoutubeModal, setShowYoutubeModal] = useState(false);
  const [karaokeChannels, setKaraokeChannels] = useState<KaraokeChannel[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [youtubeError, setYoutubeError] = useState<string | null>(null);
  const [editSong, setEditSong] = useState<SongUpdate | null>(null);
  const [filterQuery, setFilterQuery] = useState("");
  const [filterArtist, setFilterArtist] = useState("");
  const [filterStatus, setFilterStatus] = useState("");

  const fetchKaraokeChannels = useCallback(async (token: string) => {
    try {
      console.log(`Fetching karaoke channels from: ${API_ROUTES.KARAOKE_CHANNELS}`);
      const response = await fetch(API_ROUTES.KARAOKE_CHANNELS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to fetch karaoke channels: ${response.status} ${response.statusText}`);
      }
      const data: KaraokeChannel[] = await response.json();
      console.log("Karaoke Channels fetched:", data);
      setKaraokeChannels(data);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      console.error("Fetch Karaoke Channels Error:", errorMessage, err);
      setKaraokeChannels([]);
    }
  }, []);

  const fetchPendingSongs = useCallback(async (token: string) => {
    try {
      console.log(`Fetching pending songs from: ${API_ROUTES.PENDING_SONGS}`);
      const response = await fetch(API_ROUTES.PENDING_SONGS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("Pending Songs Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch pending songs: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data: PendingSong[] = JSON.parse(responseText);
      console.log("Pending Songs Parsed:", data);
      // Verify key uniqueness
      const idSet = new Set(data.map(song => song.id));
      if (idSet.size !== data.length) {
        console.warn("Duplicate song IDs detected:", data.map(song => song.id));
      }
      setPendingSongs(data);
      setError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? `${err.message} (Network error: ${err.cause ? err.cause : 'Unknown'})` : "Unknown fetch error";
      setError(errorMessage);
      setPendingSongs([]);
      console.error("Fetch Pending Songs Error:", errorMessage, err);
    }
  }, []);

  const fetchManageableSongs = useCallback(async (token: string) => {
    try {
      console.log(`Fetching manageable songs from: ${API_ROUTES.SONGS_MANAGE}`);
      const queryParams = new URLSearchParams({
        query: filterQuery,
        artist: filterArtist,
        status: filterStatus,
        page: "1",
        pageSize: "50",
      }).toString();
      const response = await fetch(`${API_ROUTES.SONGS_MANAGE}?${queryParams}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("Manageable Songs Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch manageable songs: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      const songs = data.songs || [];
      console.log("Manageable Songs Parsed:", songs);
      // Verify key uniqueness
      const idSet = new Set(songs.map((song: SongUpdate) => song.Id));
      if (idSet.size !== songs.length) {
        console.warn("Duplicate song IDs detected:", songs.map((song: SongUpdate) => song.Id));
      }
      setManageableSongs(songs);
      setError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      setManageableSongs([]);
      console.error("Fetch Manageable Songs Error:", errorMessage, err);
    }
  }, [filterQuery, filterArtist, filterStatus]);

  useEffect(() => {
    try {
      console.log("SongManagerPage useEffect running");
      const token = localStorage.getItem("token");
      const storedRoles = localStorage.getItem("roles");
      if (!token) {
        console.log("No token found, redirecting to login");
        navigate("/");
        return;
      }
      if (storedRoles) {
        const parsedRoles = JSON.parse(storedRoles);
        if (!parsedRoles.includes("Song Manager")) {
          console.log("User lacks Song Manager role, redirecting to dashboard");
          navigate("/dashboard");
          return;
        }
      }
      fetchPendingSongs(token);
      fetchManageableSongs(token);
      fetchKaraokeChannels(token);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      console.error("SongManagerPage useEffect error:", errorMessage, err);
      setError(errorMessage);
    }
  }, [navigate, fetchPendingSongs, fetchManageableSongs, fetchKaraokeChannels]);

  const handleYoutubeSearch = async (songId: number, title: string, artist: string, token: string) => {
    try {
      const query = `Karaoke ${title} ${artist}`;
      console.log(`Fetching YouTube search for song ${songId} from: ${API_ROUTES.YOUTUBE_SEARCH}`);
      const response = await fetch(`${API_ROUTES.YOUTUBE_SEARCH}?query=${encodeURIComponent(query)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log(`YouTube Search Raw Response for Song ${songId}:`, responseText);
      if (!response.ok) {
        throw new Error(`Failed to search YouTube: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data: YouTubeVideo[] = JSON.parse(responseText);
      // Verify key uniqueness
      const videoIdSet = new Set(data.map(video => video.videoId));
      if (videoIdSet.size !== data.length) {
        console.warn("Duplicate video IDs detected:", data.map(video => video.videoId));
      }
      const sortedResults = data.sort((a, b) => {
        const aChannel = karaokeChannels.find(c => c.ChannelName === a.channelTitle);
        const bChannel = karaokeChannels.find(c => c.ChannelName === b.channelTitle);
        const aOrder = aChannel ? aChannel.SortOrder : Number.MAX_SAFE_INTEGER;
        const bOrder = bChannel ? bChannel.SortOrder : Number.MAX_SAFE_INTEGER;
        return aOrder - bOrder;
      });
      console.log(`YouTube Results Parsed for Song ${songId}:`, sortedResults);
      setYoutubeResults(sortedResults);
      setSelectedSongId(songId);
      setSelectedVideo(null);
      setIsVideoEmbeddable(true);
      setShowYoutubeModal(true);
      setYoutubeError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      console.error("YouTube Search Error:", errorMessage, err);
      setYoutubeError(errorMessage);
      setYoutubeResults([]);
      setSelectedSongId(songId);
      setSelectedVideo(null);
      setIsVideoEmbeddable(true);
      setShowYoutubeModal(true);
    }
  };

  const handlePreviewVideo = (videoId: string, url: string) => {
    try {
      console.log("Previewing video:", { videoId, url });
      setSelectedVideo({ videoId, url });
      setIsVideoEmbeddable(true);
    } catch (err: unknown) {
      console.error("HandlePreviewVideo Error:", err);
    }
  };

  const handleIframeError = () => {
    try {
      console.log("Iframe error occurred");
      setIsVideoEmbeddable(false);
    } catch (err: unknown) {
      console.error("HandleIframeError Error:", err);
    }
  };

  const formatDuration = (isoDuration: string): string => {
    try {
      const match = isoDuration.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
      if (!match) return "0:00";
      const hours = parseInt(match[1] || "0", 10);
      const minutes = parseInt(match[2] || "0", 10);
      const seconds = parseInt(match[3] || "0", 10);
      if (hours > 0) {
        return `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
      }
      return `${minutes}:${seconds.toString().padStart(2, "0")}`;
    } catch (err: unknown) {
      console.error("FormatDuration Error:", err);
      return "0:00";
    }
  };

  const formatDate = (isoDate: string): string => {
    try {
      const date = new Date(isoDate);
      return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
    } catch (err: unknown) {
      console.error("FormatDate Error:", err);
      return "Unknown";
    }
  };

  const formatViewCount = (count: number): string => {
    try {
      if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`;
      if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`;
      return count.toString();
    } catch (err: unknown) {
      console.error("FormatViewCount Error:", err);
      return "0";
    }
  };

  const handleApproveSong = async (songId: number, YouTubeUrl: string, token: string) => {
    try {
      console.log(`Approving song ${songId} at: ${API_ROUTES.APPROVE_SONGS}`);
      const response = await fetch(API_ROUTES.APPROVE_SONGS, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId, YouTubeUrl }),
      });
      const responseText = await response.text();
      console.log(`Approve Song Raw Response for Song ${songId}:`, responseText);
      if (!response.ok) {
        throw new Error(`Failed to approve song: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("Song approved successfully!");
      fetchPendingSongs(token);
      fetchManageableSongs(token);
      setShowManualInput((prev) => ({ ...prev, [songId]: false }));
      setShowYoutubeModal(false);
      setYoutubeResults([]);
      setSelectedVideo(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Approve Song Error:", errorMessage, err);
    }
  };

  const handleRejectSong = async (songId: number, token: string) => {
    try {
      console.log(`Rejecting song ${songId} at: ${API_ROUTES.REJECT_SONG}`);
      const response = await fetch(API_ROUTES.REJECT_SONG, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId }),
      });
      const responseText = await response.text();
      console.log(`Reject Song Raw Response for Song ${songId}:`, responseText);
      if (!response.ok) {
        throw new Error(`Failed to reject song: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("Song rejected successfully!");
      fetchPendingSongs(token);
      fetchManageableSongs(token);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Reject Song Error:", errorMessage, err);
    }
  };

  const handleEditSong = async (token: string) => {
    if (!editSong) return;
    try {
      console.log(`Updating song ${editSong.Id} at: ${API_ROUTES.SONG_UPDATE}/${editSong.Id}`);
      const response = await fetch(`${API_ROUTES.SONG_UPDATE}/${editSong.Id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(editSong),
      });
      if (!response.ok) {
        throw new Error(`Failed to update song: ${response.status} ${response.statusText}`);
      }
      alert("Song updated successfully!");
      fetchManageableSongs(token);
      setEditSong(null);
      setError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Update Song Error:", errorMessage, err);
    }
  };

  const handleDeleteSong = async (id: number, token: string) => {
    try {
      console.log(`Deleting song ${id} at: ${API_ROUTES.SONG_DELETE}/${id}`);
      const response = await fetch(`${API_ROUTES.SONG_DELETE}/${id}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to delete song: ${response.status} ${response.statusText}`);
      }
      alert("Song deleted successfully!");
      fetchManageableSongs(token);
      setError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Delete Song Error:", errorMessage, err);
    }
  };

  const handleClearYouTubeUrl = async (id: number, token: string) => {
    try {
      console.log(`Clearing YouTube URL for song ${id} at: ${API_ROUTES.SONG_UPDATE}/${id}`);
      const song = manageableSongs.find(s => s.Id === id);
      if (!song) throw new Error("Song not found");
      const updatedSong = { ...song, YouTubeUrl: null };
      const response = await fetch(`${API_ROUTES.SONG_UPDATE}/${id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(updatedSong),
      });
      if (!response.ok) {
        throw new Error(`Failed to clear YouTube URL: ${response.status} ${response.statusText}`);
      }
      alert("YouTube URL cleared successfully!");
      fetchManageableSongs(token);
      setError(null);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Clear YouTube URL Error:", errorMessage, err);
    }
  };

  const toggleManualInput = (songId: number) => {
    try {
      console.log("Toggling manual input for song:", songId);
      setShowManualInput((prev) => ({ ...prev, [songId]: !prev[songId] }));
    } catch (err: unknown) {
      console.error("ToggleManualInput Error:", err);
    }
  };

  const handleManualLinkChange = (songId: number, value: string) => {
    try {
      console.log("Updating manual link for song:", songId, value);
      setManualLinks((prev) => ({ ...prev, [songId]: value }));
    } catch (err: unknown) {
      console.error("HandleManualLinkChange Error:", err);
    }
  };

  const token = localStorage.getItem("token") || "";

  try {
    console.log("SongManagerPage rendering");
    return (
      <div className="song-manager-container">
        <header className="song-manager-header">
          <h1 className="song-manager-title">Song Manager</h1>
          <div className="header-buttons">
            <button className="song-manager-button channels-button" onClick={() => navigate("/karaoke-channels")}>
              Manage Channels
            </button>
            <button className="song-manager-button logout-button" onClick={() => { localStorage.clear(); navigate("/"); }}>
              Logout
            </button>
            <button className="song-manager-button back-button" onClick={() => navigate("/dashboard")}>
              Back to Dashboard
            </button>
          </div>
        </header>

        <div className="song-manager-content">
          <div className="song-manager-sections">
            <section className="song-manager-card">
              <h2 className="section-title">Pending Songs</h2>
              {error && <p className="error-text">{error}</p>}
              {pendingSongs.length > 0 ? (
                <ul className="song-list">
                  {pendingSongs.map((song, index) => (
                    <li key={song.id || `pending-${index}`} className="song-item">
                      <div className="song-info">
                        <p className="song-title">{song.title} - {song.artist}</p>
                        <p className="song-text">Genre: {song.genre} | Requested by: {song.firstName} {song.lastName}</p>
                      </div>
                      <div className="song-actions">
                        <button
                          className="song-manager-button find-button"
                          onClick={() => handleYoutubeSearch(song.id, song.title, song.artist, token)}
                        >
                          Find Karaoke Video
                        </button>
                        <button
                          className="song-manager-button manual-button"
                          onClick={() => toggleManualInput(song.id)}
                        >
                          Add Manual Link
                        </button>
                        <button
                          className="song-manager-button reject-button"
                          onClick={() => handleRejectSong(song.id, token)}
                        >
                          Reject
                        </button>
                      </div>
                      {showManualInput[song.id] && (
                        <div className="manual-link-input">
                          <input
                            type="text"
                            value={manualLinks[song.id] || ""}
                            onChange={(e) => handleManualLinkChange(song.id, e.target.value)}
                            placeholder="Enter YouTube URL"
                            className="song-manager-input"
                          />
                          <button
                            className="song-manager-button approve-button"
                            onClick={() => handleApproveSong(song.id, manualLinks[song.id] || "", token)}
                          >
                            Submit Manual Link
                          </button>
                        </div>
                      )}
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="song-manager-text">No pending songs to review.</p>
              )}
            </section>

            <section className="song-editor-card">
              <h2 className="section-title">Manage Songs</h2>
              <div className="filter-section">
                <input
                  type="text"
                  value={filterQuery}
                  onChange={(e) => setFilterQuery(e.target.value)}
                  placeholder="Search by title or artist"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={filterArtist}
                  onChange={(e) => setFilterArtist(e.target.value)}
                  placeholder="Filter by artist"
                  className="song-manager-input"
                />
                <select
                  value={filterStatus}
                  onChange={(e) => setFilterStatus(e.target.value)}
                  className="song-manager-input"
                >
                  <option value="">All Statuses</option>
                  <option value="active">Active</option>
                  <option value="pending">Pending</option>
                  <option value="unavailable">Unavailable</option>
                </select>
                <button
                  className="song-manager-button filter-button"
                  onClick={() => fetchManageableSongs(token)}
                >
                  Apply Filters
                </button>
              </div>
              {error && <p className="error-text">{error}</p>}
              {manageableSongs.length > 0 ? (
                <ul className="song-list">
                  {manageableSongs.map((song, index) => (
                    <li key={song.Id || `manageable-${index}`} className="song-item">
                      <div className="song-info">
                        <p className="song-title">{song.Title} - {song.Artist}</p>
                        <p className="song-text">Genre: {song.Genre || "Unknown"} | Status: {song.Status}</p>
                      </div>
                      <div className="song-actions">
                        <button
                          className="song-manager-button edit-button"
                          onClick={() => setEditSong(song)}
                        >
                          Edit
                        </button>
                        <button
                          className="song-manager-button clear-url-button"
                          onClick={() => handleClearYouTubeUrl(song.Id, token)}
                        >
                          Clear YouTube URL
                        </button>
                        <button
                          className="song-manager-button delete-button"
                          onClick={() => handleDeleteSong(song.Id, token)}
                        >
                          Delete
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="song-manager-text">No songs match the filters.</p>
              )}
            </section>
          </div>
        </div>

        {showYoutubeModal && selectedSongId && (
          <div className="modal-overlay">
            <div className="modal-content youtube-modal">
              <h2 className="modal-title">Select Karaoke Video for {pendingSongs.find(s => s.id === selectedSongId)?.title}</h2>
              <div className="youtube-modal-content">
                <div className="youtube-list">
                  {youtubeError ? (
                    <p className="error-text">{youtubeError}</p>
                  ) : youtubeResults.length > 0 ? (
                    <ul className="youtube-results">
                      {youtubeResults.map((video, index) => (
                        <li key={video.videoId || `youtube-${index}`} className="youtube-item">
                          <div className="youtube-info">
                            <p className="youtube-title">{video.title}</p>
                            <p className="youtube-meta">Channel: {video.channelTitle}</p>
                            <p className="youtube-meta">Duration: {formatDuration(video.duration)}</p>
                            <p className="youtube-meta">Uploaded: {formatDate(video.uploadDate)}</p>
                            <p className="youtube-meta">Views: {formatViewCount(video.viewCount)}</p>
                          </div>
                          <div className="youtube-actions">
                            <button
                              className="song-manager-button preview-button"
                              onClick={() => handlePreviewVideo(video.videoId, video.url)}
                            >
                              Preview
                            </button>
                            <button
                              className="song-manager-button approve-button"
                              onClick={() => handleApproveSong(selectedSongId, video.url, token)}
                            >
                              Accept
                            </button>
                          </div>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className="song-text">No karaoke videos found.</p>
                  )}
                </div>
                <div className="youtube-preview">
                  {selectedVideo ? (
                    isVideoEmbeddable ? (
                      <iframe
                        width="300"
                        height="169"
                        src={`https://www.youtube.com/embed/${selectedVideo.videoId}`}
                        title="YouTube Preview"
                        frameBorder="0"
                        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                        allowFullScreen
                        onError={handleIframeError}
                      />
                    ) : (
                      <div className="youtube-fallback">
                        <p>Video not embeddable, please watch on YouTube.</p>
                        <a
                          href={selectedVideo.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="watch-link"
                        >
                          Watch on YouTube
                        </a>
                      </div>
                    )
                  ) : (
                    <p className="song-text">Select a video to preview.</p>
                  )}
                </div>
              </div>
              <div className="modal-buttons">
                <button className="song-manager-button close-button" onClick={() => setShowYoutubeModal(false)}>
                  Close
                </button>
              </div>
            </div>
          </div>
        )}

        {editSong && (
          <div className="modal-overlay">
            <div className="modal-content">
              <h2 className="modal-title">Edit Song</h2>
              <div className="edit-form">
                <input
                  type="text"
                  value={editSong.Title || ""}
                  onChange={(e) => setEditSong({ ...editSong, Title: e.target.value })}
                  placeholder="Title"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Artist || ""}
                  onChange={(e) => setEditSong({ ...editSong, Artist: e.target.value })}
                  placeholder="Artist"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Genre || ""}
                  onChange={(e) => setEditSong({ ...editSong, Genre: e.target.value })}
                  placeholder="Genre"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Decade || ""}
                  onChange={(e) => setEditSong({ ...editSong, Decade: e.target.value })}
                  placeholder="Decade (e.g., 1980s)"
                  className="song-manager-input"
                />
                <input
                  type="number"
                  step="0.1"
                  value={editSong.Bpm || ""}
                  onChange={(e) => setEditSong({ ...editSong, Bpm: parseFloat(e.target.value) || null })}
                  placeholder="BPM"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Danceability || ""}
                  onChange={(e) => setEditSong({ ...editSong, Danceability: e.target.value })}
                  placeholder="Danceability (e.g., danceable)"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Energy || ""}
                  onChange={(e) => setEditSong({ ...editSong, Energy: e.target.value })}
                  placeholder="Energy (e.g., aggressive)"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.Mood || ""}
                  onChange={(e) => setEditSong({ ...editSong, Mood: e.target.value })}
                  placeholder="Mood (e.g., happy)"
                  className="song-manager-input"
                />
                <input
                  type="number"
                  value={editSong.Popularity || ""}
                  onChange={(e) => setEditSong({ ...editSong, Popularity: parseInt(e.target.value) || null })}
                  placeholder="Popularity"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.SpotifyId || ""}
                  onChange={(e) => setEditSong({ ...editSong, SpotifyId: e.target.value })}
                  placeholder="Spotify ID"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.YouTubeUrl || ""}
                  onChange={(e) => setEditSong({ ...editSong, YouTubeUrl: e.target.value })}
                  placeholder="YouTube URL"
                  className="song-manager-input"
                />
                <input
                  type="text"
                  value={editSong.MusicBrainzId || ""}
                  onChange={(e) => setEditSong({ ...editSong, MusicBrainzId: e.target.value })}
                  placeholder="MusicBrainz ID"
                  className="song-manager-input"
                />
                <input
                  type="number"
                  value={editSong.LastFmPlaycount || ""}
                  onChange={(e) => setEditSong({ ...editSong, LastFmPlaycount: parseInt(e.target.value) || null })}
                  placeholder="Last.fm Playcount"
                  className="song-manager-input"
                />
                <input
                  type="number"
                  value={editSong.Valence || ""}
                  onChange={(e) => setEditSong({ ...editSong, Valence: parseInt(e.target.value) || null })}
                  placeholder="Valence"
                  className="song-manager-input"
                />
                <select
                  value={editSong.Status || ""}
                  onChange={(e) => setEditSong({ ...editSong, Status: e.target.value })}
                  className="song-manager-input"
                >
                  <option value="active">Active</option>
                  <option value="pending">Pending</option>
                  <option value="unavailable">Unavailable</option>
                </select>
              </div>
              <div className="modal-buttons">
                <button
                  className="song-manager-button save-button"
                  onClick={() => handleEditSong(token)}
                >
                  Save
                </button>
                <button
                  className="song-manager-button close-button"
                  onClick={() => setEditSong(null)}
                >
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  } catch (error: unknown) {
    console.error("SongManagerPage render error:", error);
    return <div>Error in SongManagerPage: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default SongManagerPage;
