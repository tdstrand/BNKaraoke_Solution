// src/pages/PendingRequests.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_ROUTES } from "../config/apiConfig";
import '../pages/PendingRequests.css'; // Updated import path
import { Song } from '../types';

const PendingRequests: React.FC = () => {
  const navigate = useNavigate();
  const [pendingSongs, setPendingSongs] = useState<Song[]>([]);
  const [youtubeUrl, setYoutubeUrl] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[PENDING_REQUESTS] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[PENDING_REQUESTS] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[PENDING_REQUESTS] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[PENDING_REQUESTS] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[PENDING_REQUESTS] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    fetchPendingSongs(token);
  }, [navigate]);

  const fetchPendingSongs = async (token: string) => {
    try {
      console.log(`[PENDING_REQUESTS] Fetching pending songs from: ${API_ROUTES.PENDING_SONGS}`);
      const response = await fetch(API_ROUTES.PENDING_SONGS, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log('[PENDING_REQUESTS] Pending Songs Response:', { status: response.status, body: responseText });

      if (!response.ok) {
        throw new Error(`Failed to fetch pending songs: ${response.status} - ${responseText}`);
      }

      const data: Song[] = JSON.parse(responseText);
      setPendingSongs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      setPendingSongs([]);
      console.error("[PENDING_REQUESTS] Fetch pending songs error:", err);
    }
  };

  const handleApprove = async (songId: number) => {
    const token = validateToken();
    if (!token) return;

    if (!youtubeUrl) {
      setError('Please provide a YouTube URL');
      return;
    }

    try {
      console.log(`[PENDING_REQUESTS] Approving song ${songId} at: ${API_ROUTES.APPROVE_SONGS}`);
      const response = await fetch(API_ROUTES.APPROVE_SONGS, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId, youtubeUrl }),
      });
      const responseText = await response.text();
      console.log('[PENDING_REQUESTS] Approve Song Response:', { status: response.status, body: responseText });

      if (!response.ok) {
        throw new Error(`Approve failed: ${response.status} - ${responseText}`);
      }

      const data = JSON.parse(responseText);
      alert(data.message);
      fetchPendingSongs(token);
      setYoutubeUrl('');
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("[PENDING_REQUESTS] Approve song error:", err);
    }
  };

  const handleReject = async (songId: number) => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[PENDING_REQUESTS] Rejecting song ${songId} at: ${API_ROUTES.REJECT_SONG}`);
      const response = await fetch(`${API_ROUTES.REJECT_SONG}?id=${songId}`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log('[PENDING_REQUESTS] Reject Song Response:', { status: response.status, body: responseText });

      if (!response.ok) {
        throw new Error(`Reject failed: ${response.status} - ${responseText}`);
      }

      const data = JSON.parse(responseText);
      alert(data.message);
      fetchPendingSongs(token);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("[PENDING_REQUESTS] Reject song error:", err);
    }
  };

  try {
    return (
      <div className="pending-requests-container mobile-pending-requests">
        <header className="pending-requests-header">
          <h1 className="pending-requests-title">Pending Song Requests</h1>
          <p className="pending-requests-text">Approve or reject song requests for the karaoke queue!</p>
          <div className="header-buttons">
            <button 
              className="action-button back-button" 
              onClick={() => navigate("/dashboard")}
              onTouchStart={() => navigate("/dashboard")}
            >
              Back to Dashboard
            </button>
          </div>
        </header>

        {error && <p className="error-text">{error}</p>}
        {pendingSongs.length > 0 ? (
          <ul className="song-list">
            {pendingSongs.map((song) => (
              <li key={song.id} className="song-item">
                <div className="song-info">
                  <p className="song-title">{song.title} - {song.artist}</p>
                  <p className="song-text">Spotify ID: {song.spotifyId || "N/A"}</p>
                  <p className="song-text">BPM: {song.bpm || "N/A"} | Danceability: {song.danceability || "N/A"} | Energy: {song.energy || "N/A"} | Popularity: {song.popularity || "N/A"}</p>
                  <p className="song-text">Requested by: {song.requestedBy}</p>
                </div>
                <div className="song-actions">
                  <input
                    type="text"
                    value={youtubeUrl}
                    onChange={(e) => setYoutubeUrl(e.target.value)}
                    placeholder="Enter YouTube URL"
                    className="form-input"
                  />
                  <button
                    className="action-button approve-button"
                    onClick={() => handleApprove(song.id)}
                    onTouchStart={() => handleApprove(song.id)}
                  >
                    Approve
                  </button>
                  <button
                    className="action-button reject-button"
                    onClick={() => handleReject(song.id)}
                    onTouchStart={() => handleReject(song.id)}
                  >
                    Reject
                  </button>
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <p className="pending-requests-text">No pending song requests.</p>
        )}
      </div>
    );
  } catch (error) {
    console.error("[PENDING_REQUESTS] Render error:", error);
    return <div>Error in PendingRequests: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default PendingRequests;