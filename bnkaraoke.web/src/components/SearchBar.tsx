import React from 'react';
import { useNavigate } from 'react-router-dom';

interface SearchBarProps {
  searchQuery: string;
  setSearchQuery: (query: string) => void;
  fetchSongs: () => void;
  resetSearch: () => void;
  navigate: ReturnType<typeof useNavigate>;
}

const SearchBar: React.FC<SearchBarProps> = ({ searchQuery, setSearchQuery, fetchSongs, resetSearch, navigate }) => {
  const handleSearchClick = () => {
    console.log("[SEARCH] handleSearchClick called");
    fetchSongs();
  };

  const handleSearchKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      console.log("[SEARCH] handleSearchKeyDown - Enter key pressed");
      fetchSongs();
    }
  };

  return (
    <section className="search-section">
      <div className="search-bar-container">
        <input
          type="text"
          placeholder="Search for Karaoke Songs to Sing"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          onKeyDown={handleSearchKeyDown}
          className="search-bar"
          aria-label="Search for karaoke songs"
        />
        <button
          onClick={handleSearchClick}
          onTouchStart={handleSearchClick}
          className="search-button"
          aria-label="Search"
        >
          🔍
        </button>
        <button
          onClick={resetSearch}
          onTouchStart={resetSearch}
          className="reset-button"
          aria-label="Reset search"
        >
          ❌
        </button>
      </div>
      <div className="explore-button-container">
        <button
          className="browse-songs-button"
          onClick={() => navigate('/explore-songs')}
          onTouchStart={() => navigate('/explore-songs')}
        >
          Browse Karaoke Songs
        </button>
      </div>
    </section>
  );
};

export default SearchBar;