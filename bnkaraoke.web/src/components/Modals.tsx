// src/components/Modals.tsx
import React from 'react';
import { Song, SpotifySong, EventQueueItem, Event } from '../types';
import './Modals.css';
import SongDetailsModal from './SongDetailsModal';

interface ModalsProps {
  isSearching: boolean;
  searchError: string | null;
  songs: Song[];
  spotifySongs: SpotifySong[];
  selectedSpotifySong: SpotifySong | null;
  requestedSong: SpotifySong | null;
  selectedSong: Song | null;
  showSearchModal: boolean;
  showSpotifyModal: boolean;
  showSpotifyDetailsModal: boolean;
  showRequestConfirmationModal: boolean;
  showReorderErrorModal: boolean;
  reorderError: string | null;
  fetchSpotifySongs: () => Promise<void>;
  handleSpotifySongSelect: (song: SpotifySong) => void;
  submitSongRequest: (song: SpotifySong) => Promise<void>;
  resetSearch: () => void;
  setSelectedSong: (song: Song | null) => void;
  setShowReorderErrorModal?: (show: boolean) => void;
  setShowSpotifyDetailsModal: (show: boolean) => void;
  setSearchError: (error: string | null) => void;
  setSelectedQueueId?: (queueId: number | undefined) => void;
  favorites: Song[];
  myQueues: { [eventId: number]: EventQueueItem[] };
  isSingerOnly: boolean;
  toggleFavorite?: (song: Song) => Promise<void>;
  addToEventQueue?: (song: Song, eventId: number) => Promise<void>;
  handleDeleteSong?: (eventId: number, queueId: number) => Promise<void>;
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  selectedQueueId: number | undefined;
  requestNewSong: () => void;
}

const Modals: React.FC<ModalsProps> = ({
  isSearching,
  searchError,
  songs,
  spotifySongs,
  selectedSpotifySong,
  requestedSong,
  selectedSong,
  showSearchModal,
  showSpotifyModal,
  showSpotifyDetailsModal,
  showRequestConfirmationModal,
  showReorderErrorModal,
  reorderError,
  fetchSpotifySongs,
  handleSpotifySongSelect,
  submitSongRequest,
  resetSearch,
  setSelectedSong,
  setShowReorderErrorModal,
  setShowSpotifyDetailsModal,
  setSearchError,
  setSelectedQueueId,
  favorites,
  myQueues,
  isSingerOnly,
  toggleFavorite,
  addToEventQueue,
  handleDeleteSong,
  currentEvent,
  checkedIn,
  isCurrentEventLive,
  selectedQueueId,
  requestNewSong,
}) => {
  const isSongInFavorites = (song: Song) => favorites.some(fav => fav.id === song.id);
  const isSongInQueue = currentEvent ? myQueues[currentEvent.eventId]?.some(item => item.songId === selectedSong?.id) : false;

  return (
    <>
      {showSearchModal && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Search Results</h2>
            {searchError && <p className="modal-text error-text">{searchError}</p>}
            {isSearching ? (
              <p className="modal-text">Searching...</p>
            ) : songs.length === 0 ? (
              <>
                <p className="modal-text">No songs found in the database.</p>
                <button 
                  onClick={requestNewSong} 
                  onTouchStart={requestNewSong} 
                  className="action-button"
                >
                  Request a New Song
                </button>
              </>
            ) : (
              <div className="song-list">
                {songs.map(song => (
                  <div
                    key={song.id}
                    className="song-card"
                    onClick={() => {
                      setSelectedSong(song);
                      setSearchError(null);
                    }}
                    onTouchStart={() => {
                      setSelectedSong(song);
                      setSearchError(null);
                    }}
                  >
                    <span className="song-text">{song.title} - {song.artist}</span>
                  </div>
                ))}
              </div>
            )}
            <button 
              onClick={resetSearch} 
              onTouchStart={resetSearch} 
              className="modal-cancel"
            >
              Close
            </button>
          </div>
        </div>
      )}
      {showSpotifyModal && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Spotify Results</h2>
            {isSearching ? (
              <p className="modal-text">Searching...</p>
            ) : spotifySongs.length === 0 ? (
              <p className="modal-text">No songs found on Spotify.</p>
            ) : (
              <div className="song-list">
                {spotifySongs.map(song => (
                  <div
                    key={song.id}
                    className="song-card"
                    onClick={() => handleSpotifySongSelect(song)}
                    onTouchStart={() => handleSpotifySongSelect(song)}
                  >
                    <span className="song-text">{song.title} - {song.artist}</span>
                  </div>
                ))}
              </div>
            )}
            <button 
              onClick={resetSearch} 
              onTouchStart={resetSearch} 
              className="modal-cancel"
            >
              Close
            </button>
          </div>
        </div>
      )}
      {showSpotifyDetailsModal && selectedSpotifySong && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">{selectedSpotifySong.title}</h2>
            <div className="song-details">
              <p><strong>Artist:</strong> {selectedSpotifySong.artist || 'Unknown'}</p>
              <p><strong>BPM:</strong> {selectedSpotifySong.bpm || 'N/A'}</p>
              <p><strong>Danceability:</strong> {selectedSpotifySong.danceability || 'N/A'}</p>
              <p><strong>Energy:</strong> {selectedSpotifySong.energy || 'N/A'}</p>
              {selectedSpotifySong.valence && <p><strong>Valence:</strong> {selectedSpotifySong.valence}</p>}
              {selectedSpotifySong.popularity && <p><strong>Popularity:</strong> {selectedSpotifySong.popularity}</p>}
              {selectedSpotifySong.genre && <p><strong>Genre:</strong> {selectedSpotifySong.genre}</p>}
              {selectedSpotifySong.decade && <p><strong>Decade:</strong> {selectedSpotifySong.decade}</p>}
            </div>
            <div className="song-actions">
              <button
                onClick={() => submitSongRequest(selectedSpotifySong)}
                onTouchStart={() => submitSongRequest(selectedSpotifySong)}
                className="action-button"
                disabled={isSearching}
              >
                Request Song
              </button>
              <button
                onClick={() => setShowSpotifyDetailsModal(false)}
                onTouchStart={() => setShowSpotifyDetailsModal(false)}
                className="modal-cancel"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
      {showRequestConfirmationModal && requestedSong && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Song Request Submitted</h2>
            <p className="modal-text">
              Your request for <strong>{requestedSong.title}</strong> by {requestedSong.artist} has been submitted!
            </p>
            <button 
              onClick={resetSearch} 
              onTouchStart={resetSearch} 
              className="modal-cancel"
            >
              Close
            </button>
          </div>
        </div>
      )}
      {showReorderErrorModal && reorderError && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Queue Reorder Error</h2>
            <p className="modal-text error-text">{reorderError}</p>
            <button
              onClick={() => setShowReorderErrorModal && setShowReorderErrorModal(false)}
              onTouchStart={() => setShowReorderErrorModal && setShowReorderErrorModal(false)}
              className="modal-cancel"
            >
              Close
            </button>
          </div>
        </div>
      )}
      {selectedSong && (
        <SongDetailsModal
          song={selectedSong}
          isFavorite={isSongInFavorites(selectedSong)}
          isInQueue={isSongInQueue}
          onClose={() => {
            setSelectedSong(null);
            setSearchError(null);
          }}
          onToggleFavorite={toggleFavorite}
          onAddToQueue={addToEventQueue}
          onDeleteFromQueue={handleDeleteSong}
          eventId={currentEvent?.eventId}
          queueId={selectedQueueId}
          readOnly={isSingerOnly}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
        />
      )}
    </>
  );
};

export default Modals;