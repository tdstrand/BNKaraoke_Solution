import React, { useState, useEffect, useCallback } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "../pages/Dashboard.css";
import { SpotifySong } from "../types";

const RequestSongPage: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const initialQuery = location.state && location.state.searchQuery ? location.state.searchQuery : "";
  const [searchQuery, setSearchQuery] = useState<string>(initialQuery);
  const [searchResults, setSearchResults] = useState<SpotifySong[]>([]);
  const [error, setError] = useState<string | null>(null);

  const handleSearch = useCallback(async (token: string) => {
    if (!token) {
      setError("Authentication token missing. Please log in again.");
      return;
    }
    try {
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("Request Song Search Raw Response:", responseText);
      if (!response.ok) throw new Error(`Failed to search: ${response.status} ${response.statusText} - ${responseText}`);
      const data = JSON.parse(responseText);
      setSearchResults(data.songs || []);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      setSearchResults([]);
    }
  }, [searchQuery]);

  useEffect(() => {
    if (searchQuery) {
      handleSearch(localStorage.getItem("token") || "");
    }
  }, [searchQuery, handleSearch]);

  const handleRequestSong = async (song: SpotifySong) => {
    const token = localStorage.getItem("token") || "";
    const requestBody = {
      title: song.title,
      artist: song.artist,
      spotifyId: song.id,
      bpm: song.bpm || 0,
      danceability: song.danceability || 0,
      energy: song.energy || 0,
      popularity: song.popularity || 0,
      genre: song.genre || null,
      status: "pending",
      requestDate: new Date().toISOString(),
      requestedBy: localStorage.getItem("userName") || "12345678901",
    };

    try {
      const response = await fetch(API_ROUTES.REQUEST_SONG, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(requestBody),
      });
      const responseText = await response.text();
      console.log("Request Song Raw Response:", responseText);
      if (!response.ok) throw new Error(`Failed to request song: ${response.status} ${response.statusText} - ${responseText}`);
      alert("Song requested successfully!");
      navigate("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    }
  };

  return (
    <div className="dashboard-container">
      <h1 className="dashboard-title">Request a Karaoke Song</h1>
      <div className="dashboard-card">
        <div className="search-bar">
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search for a song..."
            className="form-input"
          />
          <button onClick={() => handleSearch(localStorage.getItem("token") || "")} className="dashboard-button">
            Search
          </button>
        </div>
        {error && <p className="error-text">{error}</p>}
        {searchResults.length > 0 ? (
          <ul className="song-list">
            {searchResults.map((song) => (
              <li key={song.id} className="song-item">
                <div>
                  <p className="song-title">{song.title} - {song.artist}</p>
                  <p className="song-text">Genre: {song.genre || "Unknown"}</p>
                </div>
                <button
                  className="dashboard-button action-button"
                  onClick={() => handleRequestSong(song)}
                >
                  Request This Song
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="dashboard-text">No results yet. Search for a song!</p>
        )}
      </div>
    </div>
  );
};

export default RequestSongPage;