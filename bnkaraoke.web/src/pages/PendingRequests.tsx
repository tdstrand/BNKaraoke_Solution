import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_ROUTES } from "../config/apiConfig";
import '../components/Home.css';
import { Song } from '../types';

const PendingRequests: React.FC = () => {
  const navigate = useNavigate();
  const [pendingSongs, setPendingSongs] = useState<Song[]>([]);
  const [youtubeUrl, setYoutubeUrl] = useState<string>('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const token = localStorage.getItem('token');
    if (!token) {
      navigate('/');
      return;
    }
    fetchPendingSongs(token);
  }, [navigate]);

  const fetchPendingSongs = async (token: string) => {
    try {
      const response = await fetch(API_ROUTES.PENDING_SONGS, {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log('Pending Songs Response:', { status: response.status, body: responseText });

      if (!response.ok) {
        throw new Error(`Failed to fetch pending songs: ${response.status} - ${responseText}`);
      }

      const data: Song[] = JSON.parse(responseText);
      setPendingSongs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      setPendingSongs([]);
    }
  };

  const handleApprove = async (songId: number) => {
    const token = localStorage.getItem('token');
    if (!token || !youtubeUrl) {
      setError('Please provide a YouTube URL');
      return;
    }

    try {
      const response = await fetch(API_ROUTES.APPROVE_SONGS, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ id: songId, youtubeUrl }),
      });
      const responseText = await response.text();
      console.log('Approve Song Response:', { status: response.status, body: responseText });

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
    }
  };

  const handleReject = async (songId: number) => {
    const token = localStorage.getItem('token');
    if (!token) {
      navigate('/');
      return;
    }

    try {
      const response = await fetch(`${API_ROUTES.REJECT_SONG}?id=${songId}`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log('Reject Song Response:', { status: response.status, body: responseText });

      if (!response.ok) {
        throw new Error(`Reject failed: ${response.status} - ${responseText}`);
      }

      const data = JSON.parse(responseText);
      alert(data.message);
      fetchPendingSongs(token);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    }
  };

  return (
    <div className="home-container">
      <header className="home-header">
        <h1>Pending Song Requests</h1>
        <p>Approve or reject song requests for the karaoke queue!</p>
      </header>

      {error && <p style={{ color: '#FF6B6B' }}>{error}</p>}
      {pendingSongs.length > 0 ? (
        <ul style={{ listStyle: 'none', padding: 0 }}>
          {pendingSongs.map((song) => (
            <li
              key={song.id}
              style={{ background: '#fff', color: '#000', padding: '15px', borderRadius: '8px', marginBottom: '10px', boxShadow: '0 2px 4px rgba(0,0,0,0.1)', display: 'flex', flexDirection: 'column', gap: '10px' }}
            >
              <div>
                <p style={{ fontWeight: 'bold' }}>{song.title} - {song.artist}</p>
                <p>Spotify ID: {song.spotifyId || "N/A"}</p>
                <p>BPM: {song.bpm || "N/A"} | Danceability: {song.danceability || "N/A"} | Energy: {song.energy || "N/A"} | Popularity: {song.popularity || "N/A"}</p>
                <p>Requested by: {song.requestedBy}</p>
              </div>
              <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
                <input
                  type="text"
                  value={youtubeUrl}
                  onChange={(e) => setYoutubeUrl(e.target.value)}
                  placeholder="Enter YouTube URL"
                  style={{ flex: 1, padding: '5px', borderRadius: '4px', border: '1px solid #ccc' }}
                />
                <button
                  className="menu-item"
                  style={{ padding: '5px 10px' }}
                  onClick={() => handleApprove(song.id)}
                >
                  Approve
                </button>
                <button
                  className="menu-item"
                  style={{ padding: '5px 10px', background: '#FF6B6B' }}
                  onClick={() => handleReject(song.id)}
                >
                  Reject
                </button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <p>No pending song requests.</p>
      )}
    </div>
  );
};

export default PendingRequests;