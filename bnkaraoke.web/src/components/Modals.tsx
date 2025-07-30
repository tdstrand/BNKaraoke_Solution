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
  showAlreadyExistsModal?: boolean;
  alreadyExistsError?: string | null;
  showReorderErrorModal: boolean;
  reorderError: string | null;
  fetchSpotifySongs: () => Promise<void>;
  handleSpotifySongSelect: (song: SpotifySong) => void;
  submitSongRequest: (song: SpotifySong) => Promise<void>;
  resetSearch: () => void;
  setSelectedSong: (song: Song | null) => void;
  setShowReorderErrorModal?: (show: boolean) => void;
  setShowSpotifyModal: (show: boolean) => void;
  setShowSpotifyDetailsModal: (show: boolean) => void;
  setShowRequestConfirmationModal: (show: boolean) => void;
  setShowAlreadyExistsModal?: (show: boolean) => void;
  setAlreadyExistsError?: (error: string | null) => void;
  setRequestedSong: (song: SpotifySong | null) => void;
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
  showAlreadyExistsModal,
  alreadyExistsError,
  showReorderErrorModal,
  reorderError,
  fetchSpotifySongs,
  handleSpotifySongSelect,
  submitSongRequest,
  resetSearch,
  setSelectedSong,
  setShowReorderErrorModal,
  setShowSpotifyModal,
  setShowSpotifyDetailsModal,
  setShowRequestConfirmationModal,
  setShowAlreadyExistsModal,
  setAlreadyExistsError,
  setRequestedSong,
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
              <p className="modal-text">No songs found in the database.</p>
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
                    onTouchEnd={() => {
                      setSelectedSong(song);
                      setSearchError(null);
                    }}
                  >
                    <div className="song-title">{song.title}</div>
                    <div className="song-artist">({song.artist || 'Unknown Artist'})</div>
                    {song.status && (
                      <div className="song-status">
                        {song.status.toLowerCase() === 'active' && (
                          <span className="song-status-badge available">Available</span>
                        )}
                        {song.status.toLowerCase() === 'pending' && (
                          <span className="song-status-badge pending">Pending</span>
                        )}
                        {song.status.toLowerCase() === 'unavailable' && (
                          <span className="song-status-badge unavailable">Unavailable</span>
                        )}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
            <div className="modal-actions">
              <button
                onClick={fetchSpotifySongs}
                onTouchEnd={fetchSpotifySongs}
                className="action-button"
                disabled={isSearching}
              >
                Request New Song
              </button>
              <button
                onClick={resetSearch}
                onTouchEnd={resetSearch}
                className="modal-cancel"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
      {showSpotifyModal && (
        <div className="modal-overlay secondary-modal mobile-spotify-modal">
          <div className="modal-content spotify-modal">
            <h2 className="modal-title">Request a New Song</h2>
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
                    onTouchEnd={() => handleSpotifySongSelect(song)}
                  >
                    <div className="song-title">{song.title}</div>
                    <div className="song-artist">({song.artist || 'Unknown Artist'})</div>
                  </div>
                ))}
              </div>
            )}
            <div className="modal-actions">
              <button
                onClick={() => setShowSpotifyModal(false)}
                onTouchEnd={() => setShowSpotifyModal(false)}
                className="modal-cancel"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
      {showSpotifyDetailsModal && selectedSpotifySong && (
        <div className="modal-overlay secondary-modal mobile-spotify-details-modal">
          <div className="modal-content spotify-details-modal">
            <h2 className="modal-title">{selectedSpotifySong.title}</h2>
            <div className="song-details">
              <p className="modal-text"><strong>Artist:</strong> {selectedSpotifySong.artist || 'Unknown'}</p>
              <p className="modal-text"><strong>BPM:</strong> {selectedSpotifySong.bpm || 'N/A'}</p>
              <p className="modal-text"><strong>Danceability:</strong> {selectedSpotifySong.danceability || 'N/A'}</p>
              <p className="modal-text"><strong>Energy:</strong> {selectedSpotifySong.energy || 'N/A'}</p>
              {selectedSpotifySong.valence && (
                <p className="modal-text"><strong>Valence:</strong> {selectedSpotifySong.valence}</p>
              )}
              {selectedSpotifySong.popularity && (
                <p className="modal-text"><strong>Popularity:</strong> {selectedSpotifySong.popularity}</p>
              )}
              {selectedSpotifySong.genre && (
                <p className="modal-text"><strong>Genre:</strong> {selectedSpotifySong.genre}</p>
              )}
              {selectedSpotifySong.decade && (
                <p className="modal-text"><strong>Decade:</strong> {selectedSpotifySong.decade}</p>
              )}
            </div>
            <div className="modal-actions">
              <button
                onClick={() => submitSongRequest(selectedSpotifySong)}
                onTouchEnd={() => submitSongRequest(selectedSpotifySong)}
                className="action-button"
                disabled={isSearching}
              >
                Request Song
              </button>
              <button
                onClick={() => setShowSpotifyDetailsModal(false)}
                onTouchEnd={() => setShowSpotifyDetailsModal(false)}
                className="modal-cancel"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
      {showRequestConfirmationModal && requestedSong && (
        <div className="modal-overlay secondary-modal mobile-request-confirmation-modal">
          <div className="modal-content request-confirmation-modal">
            <h2 className="modal-title">Song Request Submitted</h2>
            <p className="modal-text">
              Your request for <strong>{requestedSong.title}</strong> by {requestedSong.artist} has been submitted!
            </p>
            <div className="modal-actions">
              <button
                onClick={() => {
                  setShowRequestConfirmationModal(false);
                  setRequestedSong(null);
                  setShowSpotifyModal(false);
                }}
                onTouchEnd={() => {
                  setShowRequestConfirmationModal(false);
                  setRequestedSong(null);
                  setShowSpotifyModal(false);
                }}
                className="modal-cancel"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
      {showAlreadyExistsModal && alreadyExistsError && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Song Already Exists</h2>
            <p className="modal-text">{alreadyExistsError}</p>
            <div className="modal-actions">
              <button
                onClick={() => {
                  setShowAlreadyExistsModal && setShowAlreadyExistsModal(false);
                  setAlreadyExistsError && setAlreadyExistsError(null);
                }}
                onTouchEnd={() => {
                  setShowAlreadyExistsModal && setShowAlreadyExistsModal(false);
                  setAlreadyExistsError && setAlreadyExistsError(null);
                }}
                className="modal-cancel"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
      {showReorderErrorModal && reorderError && (
        <div className="modal-overlay mobile-modals">
          <div className="modal-content">
            <h2 className="modal-title">Queue Reorder Error</h2>
            <p className="modal-text error-text">{reorderError}</p>
            <div className="modal-actions">
              <button
                onClick={() => setShowReorderErrorModal && setShowReorderErrorModal(false)}
                onTouchEnd={() => setShowReorderErrorModal && setShowReorderErrorModal(false)}
                className="modal-cancel"
              >
                Close
              </button>
            </div>
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
          onToggleFavorite={selectedSong.status?.toLowerCase() === 'active' ? toggleFavorite : undefined}
          onAddToQueue={selectedSong.status?.toLowerCase() === 'active' ? addToEventQueue : undefined}
          onDeleteFromQueue={selectedSong.status?.toLowerCase() === 'active' ? handleDeleteSong : undefined}
          eventId={currentEvent?.eventId}
          queueId={selectedQueueId}
          readOnly={isSingerOnly || selectedSong.status?.toLowerCase() !== 'active'}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
        />
      )}
    </>
  );
};

export default Modals;