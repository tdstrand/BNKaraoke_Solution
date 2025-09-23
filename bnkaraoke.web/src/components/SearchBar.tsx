import React from 'react';
import { useNavigate } from 'react-router-dom';
import { SearchOutlined, CloseOutlined, LoadingOutlined } from '@ant-design/icons';
import './SearchBar.css';
interface SearchBarProps {
  searchQuery: string;
  setSearchQuery: (query: string) => void;
  fetchSongs: () => void;
  resetSearch: () => void;
  navigate: ReturnType<typeof useNavigate>;
  isSearching: boolean;
}
const SearchBar: React.FC<SearchBarProps> = ({ searchQuery, setSearchQuery, fetchSongs, resetSearch, navigate, isSearching }) => {
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
    <section className="search-section mobile-search-bar">
      <div className="search-bar-container">
        <div className="search-input-wrapper">
          <input
            type="text"
            placeholder="Search for Karaoke Songs to Sing"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            onKeyDown={handleSearchKeyDown}
            className="search-bar"
            aria-label="Search for karaoke songs"
            disabled={isSearching}
          />
        </div>
        <div className="search-actions">
          <button
            type="button"
            onClick={handleSearchClick}
            onTouchEnd={handleSearchClick}
            className="search-button"
            aria-label="Search"
            disabled={isSearching}
            aria-busy={isSearching}
          >
            {isSearching ? (
              <>
                <LoadingOutlined className="button-icon" spin />
                <span className="button-label">Searching…</span>
              </>
            ) : (
              <>
                <SearchOutlined className="button-icon" />
                <span className="button-label">Search</span>
              </>
            )}
          </button>
          <button
            type="button"
            onClick={resetSearch}
            onTouchEnd={resetSearch}
            className="reset-button"
            aria-label="Reset search"
            disabled={isSearching}
          >
            <CloseOutlined className="button-icon" />
            <span className="button-label">Clear</span>
          </button>
        </div>
      </div>
      <div className="explore-button-container">
        <button
          className="browse-songs-button"
          onClick={() => navigate('/explore-songs')}
          onTouchEnd={() => navigate('/explore-songs')}
          disabled={isSearching}
        >
          Browse Karaoke Songs
        </button>
      </div>
    </section>
  );
};
export default SearchBar;