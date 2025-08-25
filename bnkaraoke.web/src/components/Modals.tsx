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
  fetchSpotifySongs: (query: string) => Promise<void>;
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
  toggleFavorite?: (song: Song) => Promise<void>;
  addToEventQueue?: (song: Song, eventId: number) => Promise<void>;
  handleDeleteSong?: (eventId: number, queueId: number) => Promise<void>;
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  selectedQueueId: number | undefined;
  requestNewSong: (query: string) => Promise<void>;
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
  // Treat songs with no status as actionable so search results work correctly
  const isSongActionable = selectedSong
    ? !selectedSong.status || ['active', 'available'].includes(selectedSong.status.toLowerCase())
    : false;

  // Map SpotifySong to Song interface for consistent modal display
  const mapSpotifySongToSong = (spotifySong: SpotifySong): Song => ({
    id: parseInt(spotifySong.id, 10) || 0, // Temporary ID, not used in queue/favorites
    title: spotifySong.title || 'Unknown Title',
    artist: spotifySong.artist || 'Unknown Artist',
    genre: spotifySong.genre || undefined,
    decade: spotifySong.decade || undefined,
    status: undefined, // No status for Spotify songs not in database
    requestedBy: localStorage.getItem('userName') || undefined,
    popularity: spotifySong.popularity || undefined,
    youTubeUrl: undefined,
    spotifyId: spotifySong.id,
    approvedBy: undefined,
    bpm: spotifySong.bpm || undefined,
    requestDate: undefined,
    musicBrainzId: undefined,
    mood: undefined,
    lastFmPlaycount: undefined,
    danceability: spotifySong.danceability || undefined,
    energy: spotifySong.energy || undefined,
    valence: spotifySong.valence || undefined,
  });

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
                onClick={() => requestNewSong('')}
                onTouchEnd={() => requestNewSong('')}
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
        <SongDetailsModal
          song={mapSpotifySongToSong(selectedSpotifySong)}
          isFavorite={false} // Spotify songs are not in favorites
          isInQueue={false} // Spotify songs are not in queue
          onClose={() => setShowSpotifyDetailsModal(false)}
          onToggleFavorite={undefined} // No favorite action for Spotify songs
          onAddToQueue={undefined} // No queue action for Spotify songs
          onRequestSong={() => submitSongRequest(selectedSpotifySong)}
          eventId={currentEvent?.eventId}
          readOnly={false} // Allow request action
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
        />
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
          onToggleFavorite={isSongActionable ? toggleFavorite : undefined}
          onAddToQueue={isSongActionable ? addToEventQueue : undefined}
          onDeleteFromQueue={isSongActionable ? handleDeleteSong : undefined}
          eventId={currentEvent?.eventId}
          queueId={selectedQueueId}
          readOnly={!isSongActionable}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
        />
      )}
    </>
  );
};

export default Modals;