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
        <button
          onClick={handleSearchClick}
          className="search-button"
          aria-label="Search"
          disabled={isSearching}
        >
          {isSearching ? <LoadingOutlined style={{ fontSize: '24px' }} /> : <SearchOutlined style={{ fontSize: '24px' }} />}
        </button>
        <button
          onClick={resetSearch}
          className="reset-button"
          aria-label="Reset search"
          disabled={isSearching}
        >
          <CloseOutlined style={{ fontSize: '24px' }} />
        </button>
      </div>
      <div className="explore-button-container">
        <button
          className="browse-songs-button"
          onClick={() => navigate('/explore-songs')}
          
          disabled={isSearching}
        >
          Browse Karaoke Songs
        </button>
      </div>
    </section>
  );
};
export default SearchBar;