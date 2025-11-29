// src/components/FavoritesSection.tsx
import React, { useCallback, useRef } from 'react';
import { Song } from '../types';
import './FavoritesSection.css';

interface FavoritesSectionProps {
  favorites: Song[];
  setSelectedSong: (song: Song | null) => void;
}

const FavoritesSection: React.FC<FavoritesSectionProps> = ({ favorites, setSelectedSong }) => {
  const touchStateRef = useRef<{ x: number; y: number; moved: boolean }>({ x: 0, y: 0, moved: false });

  const handleTouchStart = useCallback((e: React.TouchEvent<HTMLElement>) => {
    const touch = e.changedTouches?.[0];
    if (!touch) return;
    touchStateRef.current = { x: touch.clientX, y: touch.clientY, moved: false };
  }, []);

  const handleTouchMove = useCallback((e: React.TouchEvent<HTMLElement>) => {
    const touch = e.changedTouches?.[0];
    if (!touch) return;
    const dx = Math.abs(touch.clientX - touchStateRef.current.x);
    const dy = Math.abs(touch.clientY - touchStateRef.current.y);
    if (dx > 8 || dy > 8) {
      touchStateRef.current.moved = true;
    }
  }, []);

  const handleTouchEnd = useCallback((song: Song) => {
    if (touchStateRef.current.moved) return;
    setSelectedSong(song);
  }, [setSelectedSong]);

  return (
    <section className="favorites-section mobile-favorites">
      <h2>Your Favorites</h2>
      {favorites.length === 0 ? (
        <p>No favorites added yet.</p>
      ) : (
        <ul className="favorites-list">
          {favorites.map(song => (
            <li
              key={song.id}
              className="favorite-song"
              onClick={() => {
                console.log("[FAVORITE] Favorite song clicked to open SongDetailsModal with song:", song);
                setSelectedSong(song);
              }}
              onTouchStart={handleTouchStart}
              onTouchMove={handleTouchMove}
              onTouchEnd={() => handleTouchEnd(song)}
            >
              <span>{song.title} - {song.artist}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
};

export default FavoritesSection;
