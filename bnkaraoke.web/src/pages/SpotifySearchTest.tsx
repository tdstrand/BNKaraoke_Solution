// src/pages/SpotifySearchTest.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_ROUTES } from '../config/apiConfig';
import '../components/Home.css';
import { SpotifySong } from '../types';

const SpotifySearchTest: React.FC = () => {
  const navigate = useNavigate();
  const [searchTerm, setSearchTerm] = useState('');
  const [results, setResults] = useState<SpotifySong[]>([]);
  const [error, setError] = useState<string | null>(null);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[SPOTIFY_SEARCH_TEST] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[SPOTIFY_SEARCH_TEST] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[SPOTIFY_SEARCH_TEST] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[SPOTIFY_SEARCH_TEST] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[SPOTIFY_SEARCH_TEST] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  useEffect(() => {
    validateToken();
  }, [navigate]);

  const handleSearch = async () => {
    const token = validateToken();
    if (!token) return;

    const url = `${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchTerm)}`;
    console.log('[SPOTIFY_SEARCH_TEST] Fetching URL:', url);

    try {
      const response = await fetch(url, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });

      const responseText = await response.text();
      console.log('[SPOTIFY_SEARCH_TEST] Spotify API Response:', {
        status: response.status,
        body: responseText,
      });

      if (!response.ok) {
        throw new Error(`API error: ${response.status} - ${responseText}`);
      }

      const data = JSON.parse(responseText);
      setResults(data.songs || []);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      setResults([]);
      console.error("[SPOTIFY_SEARCH_TEST] Search error:", err);
    }
  };

  const handleRequest = async (song: SpotifySong) => {
    const token = validateToken();
    if (!token) return;

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
      requestedBy: localStorage.getItem("userName") || "12345678901"
    };

    try {
      console.log(`[SPOTIFY_SEARCH_TEST] Requesting song: ${song.title} by ${song.artist}`);
      const response = await fetch(API_ROUTES.REQUEST_SONG, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(requestBody),
      });

      const responseText = await response.text();
      console.log('[SPOTIFY_SEARCH_TEST] Request Song Response:', {
        status: response.status,
        body: responseText,
      });

      if (!response.ok) {
        throw new Error(`Request failed: ${response.status} - ${responseText}`);
      }

      const data = JSON.parse(responseText);
      setError(null);
      alert(data.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("[SPOTIFY_SEARCH_TEST] Request song error:", err);
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  try {
    return (
      <div className="home-container">
        <header className="home-header">
          <h1>Spotify Search Test</h1>
          <p>Search for your favorite karaoke songs!</p>
          <div className="header-buttons">
            <button className="action-button back-button" onClick={() => navigate("/dashboard")}>
              Back to Dashboard
            </button>
          </div>
        </header>

        <div className="menu">
          <input
            type="text"
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            onKeyPress={handleKeyPress}
            placeholder="Enter song or artist (e.g., Bohemian)"
            style={{ flex: 1, padding: '10px', borderRadius: '8px 0 0 8px', border: 'none', color: '#000' }}
          />
          <button
            onClick={handleSearch}
            className="menu-item"
            style={{ borderRadius: '0 8px 8px 0' }}
          >
            Search
          </button>
        </div>

        {error && <p style={{ color: '#FF6B6B' }}>{error}</p>}
        {results.length > 0 ? (
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {results.map((song) => (
              <li
                key={song.id}
                style={{ background: '#fff', color: '#000', padding: '15px', borderRadius: '8px', marginBottom: '10px', boxShadow: '0 2px 4px rgba(0,0,0,0.1)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}
              >
                <div>
                  <p style={{ fontWeight: 'bold' }}>{song.title} - {song.artist}</p>
                  <p>Spotify ID: {song.id}</p>
                  <p>Genre: {song.genre || "N/A"} | Popularity: {song.popularity || "N/A"}</p>
                </div>
                <button
                  className="menu-item"
                  style={{ padding: '5px 10px' }}
                  onClick={() => handleRequest(song)}
                >
                  Request
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p>No results yet. Enter a search term and click Search!</p>
        )}
      </div>
    );
  } catch (error) {
    console.error("[SPOTIFY_SEARCH_TEST] Render error:", error);
    return <div>Error in SpotifySearchTest: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default SpotifySearchTest;