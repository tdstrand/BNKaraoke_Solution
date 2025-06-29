import React from 'react';
import { Song } from '../types';

interface FavoritesSectionProps {
  favorites: Song[];
  setSelectedSong: (song: Song | null) => void;
}

const FavoritesSection: React.FC<FavoritesSectionProps> = ({ favorites, setSelectedSong }) => {
  return (
    <section className="favorites-section">
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