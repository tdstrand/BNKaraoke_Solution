// src/pages/PendingSongManagerPage.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./PendingSongManagerPage.css";

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

const PendingSongManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [pendingSongs, setPendingSongs] = useState<PendingSong[]>([]);
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

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split(".").length !== 3) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split(".")[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      return token;
    } catch (err) {
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  const fetchKaraokeChannels = useCallback(async (token: string) => {
    try {
      const response = await fetch(API_ROUTES.KARAOKE_CHANNELS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to fetch karaoke channels: ${response.status} ${response.statusText}`);
      }
      const data: KaraokeChannel[] = await response.json();
      setKaraokeChannels(data);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  }, []);

  const fetchPendingSongs = useCallback(async (token: string) => {
    try {
      const response = await fetch(API_ROUTES.PENDING_SONGS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Failed to fetch pending songs: ${response.status} ${response.statusText} - ${text}`);
      }
      setPendingSongs(JSON.parse(text));
      setError(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setPendingSongs([]);
      setError(message);
    }
  }, []);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const storedRoles = localStorage.getItem("roles");
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("Song Manager")) {
        navigate("/dashboard");
        return;
      }
    } else {
      navigate("/login");
      return;
    }

    fetchPendingSongs(token);
    fetchKaraokeChannels(token);
  }, [navigate, fetchPendingSongs, fetchKaraokeChannels]);

  const handleYoutubeSearch = async (songId: number, title: string, artist: string, token: string) => {
    try {
      const query = `Karaoke ${title} ${artist}`;
      const response = await fetch(`${API_ROUTES.YOUTUBE_SEARCH}?query=${encodeURIComponent(query)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Failed to search YouTube: ${response.status} ${response.statusText} - ${text}`);
      }
      const data: YouTubeVideo[] = JSON.parse(text);
      const sorted = data.sort((a, b) => {
        const aChannel = karaokeChannels.find(c => c.ChannelName === a.channelTitle);
        const bChannel = karaokeChannels.find(c => c.ChannelName === b.channelTitle);
        const aOrder = aChannel ? aChannel.SortOrder : Number.MAX_SAFE_INTEGER;
        const bOrder = bChannel ? bChannel.SortOrder : Number.MAX_SAFE_INTEGER;
        return aOrder - bOrder;
      });
      setYoutubeResults(sorted);
      setSelectedSongId(songId);
      setSelectedVideo(null);
      setIsVideoEmbeddable(true);
      setShowYoutubeModal(true);
      setYoutubeError(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setYoutubeError(message);
      setYoutubeResults([]);
      setSelectedSongId(songId);
      setSelectedVideo(null);
      setIsVideoEmbeddable(true);
      setShowYoutubeModal(true);
    }
  };

  const handlePreviewVideo = (videoId: string, url: string) => {
    setSelectedVideo({ videoId, url });
    setIsVideoEmbeddable(true);
  };

  const handleIframeError = () => {
    setIsVideoEmbeddable(false);
  };

  const formatDuration = (isoDuration: string): string => {
    const match = isoDuration.match(/PT(?:\d+H)?(?:\d+M)?(?:\d+S)?/);
    if (!match) return "0:00";
    const hours = parseInt(match[0].match(/(\d+)H/)?.[1] || "0", 10);
    const minutes = parseInt(match[0].match(/(\d+)M/)?.[1] || "0", 10);
    const seconds = parseInt(match[0].match(/(\d+)S/)?.[1] || "0", 10);
    if (hours > 0) {
      return `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
    }
    return `${minutes}:${seconds.toString().padStart(2, "0")}`;
  };

  const formatDate = (isoDate: string): string => {
    const date = new Date(isoDate);
    return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
  };

  const formatViewCount = (count: number): string => {
    if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`;
    if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`;
    return count.toString();
  };

  const handleApproveSong = async (songId: number, YouTubeUrl: string, token: string) => {
    try {
      const response = await fetch(API_ROUTES.APPROVE_SONGS, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId, YouTubeUrl }),
      });
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Failed to approve song: ${response.status} ${response.statusText} - ${text}`);
      }
      alert("Song approved successfully!");
      fetchPendingSongs(token);
      setShowManualInput(prev => ({ ...prev, [songId]: false }));
      setShowYoutubeModal(false);
      setYoutubeResults([]);
      setSelectedVideo(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const handleRejectSong = async (songId: number, token: string) => {
    try {
      const response = await fetch(API_ROUTES.REJECT_SONG, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId }),
      });
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Failed to reject song: ${response.status} ${response.statusText} - ${text}`);
      }
      alert("Song rejected successfully!");
      fetchPendingSongs(token);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const toggleManualInput = (songId: number) => {
    setShowManualInput(prev => ({ ...prev, [songId]: !prev[songId] }));
  };

  const handleManualLinkChange = (songId: number, value: string) => {
    setManualLinks(prev => ({ ...prev, [songId]: value }));
  };

  return (
    <div className="song-manager-container pending-song-manager">
      <header className="song-manager-header">
        <h1 className="song-manager-title">Pending Song Manager</h1>
        <div className="header-buttons">
          <button
            className="song-manager-button channels-button"
            onClick={() => navigate("/karaoke-channels")}
            onTouchStart={() => navigate("/karaoke-channels")}
          >
            Manage Channels
          </button>
          <button
            className="song-manager-button back-button"
            onClick={() => navigate("/dashboard")}
            onTouchStart={() => navigate("/dashboard")}
          >
            Back to Dashboard
          </button>
        </div>
      </header>

      <div className="song-manager-content">
        <section className="song-manager-card">
          <h2 className="section-title">Pending Songs</h2>
          {error && <p className="error-text">{error}</p>}
          {pendingSongs.length > 0 ? (
            <ul className="song-list">
              {pendingSongs.map(song => (
                <li key={song.id} className="song-item">
                  <div className="song-info">
                    <p className="song-title">{song.title} - {song.artist}</p>
                    <p className="song-text">Genre: {song.genre} | Requested by: {song.firstName} {song.lastName}</p>
                  </div>
                  <div className="song-actions">
                    <button
                      className="song-manager-button find-button"
                      onClick={() => {
                        const token = validateToken();
                        if (token) handleYoutubeSearch(song.id, song.title, song.artist, token);
                      }}
                      onTouchStart={() => {
                        const token = validateToken();
                        if (token) handleYoutubeSearch(song.id, song.title, song.artist, token);
                      }}
                    >
                      Find Karaoke
                    </button>
                    <button
                      className="song-manager-button manual-button"
                      onClick={() => toggleManualInput(song.id)}
                      onTouchStart={() => toggleManualInput(song.id)}
                    >
                      Manual Link
                    </button>
                    <button
                      className="song-manager-button reject-button"
                      onClick={() => {
                        const token = validateToken();
                        if (token) handleRejectSong(song.id, token);
                      }}
                      onTouchStart={() => {
                        const token = validateToken();
                        if (token) handleRejectSong(song.id, token);
                      }}
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
                        onClick={() => {
                          const token = validateToken();
                          if (token) handleApproveSong(song.id, manualLinks[song.id] || "", token);
                        }}
                        onTouchStart={() => {
                          const token = validateToken();
                          if (token) handleApproveSong(song.id, manualLinks[song.id] || "", token);
                        }}
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
      </div>

      {showYoutubeModal && selectedSongId && (
        <div className="modal-overlay pending-song-manager">
          <div className="modal-content youtube-modal">
            <h2 className="modal-title">Select Karaoke Video for {pendingSongs.find(s => s.id === selectedSongId)?.title}</h2>
            <div className="youtube-modal-content">
              <div className="youtube-list">
                {youtubeError ? (
                  <p className="error-text">{youtubeError}</p>
                ) : youtubeResults.length > 0 ? (
                  <ul className="youtube-results">
                    {youtubeResults.map(video => (
                      <li key={video.videoId} className="youtube-item">
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
                            onTouchStart={() => handlePreviewVideo(video.videoId, video.url)}
                          >
                            Preview
                          </button>
                          <button
                            className="song-manager-button approve-button"
                            onClick={() => {
                              const token = validateToken();
                              if (token) handleApproveSong(selectedSongId, video.url, token);
                            }}
                            onTouchStart={() => {
                              const token = validateToken();
                              if (token) handleApproveSong(selectedSongId, video.url, token);
                            }}
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
              <button
                className="song-manager-button close-button"
                onClick={() => setShowYoutubeModal(false)}
                onTouchStart={() => setShowYoutubeModal(false)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default PendingSongManagerPage;

