import React from 'react';
import SongDetailsModal from './SongDetailsModal';
import { Song, SpotifySong, Event, EventQueueItem } from '../types';

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
  fetchSpotifySongs: () => void;
  handleSpotifySongSelect: (song: SpotifySong) => void;
  submitSongRequest: (song: SpotifySong) => void;
  resetSearch: () => void;
  setSelectedSong: (song: Song | null) => void;
  setShowReorderErrorModal: (show: boolean) => void;
  setShowSpotifyDetailsModal: (show: boolean) => void;
  setSearchError: (error: string | null) => void;
  setSelectedQueueId: (queueId: number | undefined) => void;
  favorites: Song[];
  myQueues: { [eventId: number]: EventQueueItem[] };
  isSingerOnly: boolean;
  toggleFavorite?: (song: Song) => Promise<void>;
  addToEventQueue?: (song: Song, eventId: number) => Promise<void>;
  handleDeleteSong?: (eventId: number, queueId: number) => Promise<void>;
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  selectedQueueId?: number;
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
}) => {
  return (
    <>
      {showSearchModal && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h3 className="modal-title">Search Results</h3>
            {isSearching ? (
              <p className="modal-text">Loading...</p>
            ) : searchError ? (
              <>
                <p className="modal-text error-text">{searchError}</p>
                <div className="song-actions">
                  <button onClick={fetchSpotifySongs} className="action-button">Yes</button>
                  <button onClick={resetSearch} className="action-button">No</button>
                </div>
              </>
            ) : songs.length === 0 ? (
              <p className="modal-text">No active songs found</p>
            ) : (
              <div className="song-list">
                {songs.map(song => (
                  <div key={song.id} className="song-card" onClick={() => setSelectedSong(song)}>
                    <span className="song-text">{song.title} - {song.artist}</span>
                  </div>
                ))}
              </div>
            )}
            {!searchError && (
              <button onClick={resetSearch} className="modal-cancel">Done</button>
            )}
          </div>
        </div>
      )}

      {showSpotifyModal && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h3 className="modal-title">Spotify Search Results</h3>
            {spotifySongs.length === 0 ? (
              <p className="modal-text">No songs found on Spotify</p>
            ) : (
              <div className="song-list">
                {spotifySongs.map(song => (
                  <div key={song.id} className="song-card" onClick={() => handleSpotifySongSelect(song)}>
                    <span className="song-text">{song.title} - {song.artist}</span>
                  </div>
                ))}
              </div>
            )}
            <button onClick={resetSearch} className="modal-cancel">Done</button>
          </div>
        </div>
      )}

      {showSpotifyDetailsModal && selectedSpotifySong && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h3 className="modal-title">{selectedSpotifySong.title}</h3>
            <div className="song-details">
              <p className="modal-text"><strong>Artist:</strong> {selectedSpotifySong.artist}</p>
              {selectedSpotifySong.genre && <p className="modal-text"><strong>Genre:</strong> {selectedSpotifySong.genre}</p>}
              {selectedSpotifySong.popularity && <p className="modal-text"><strong>Popularity:</strong> {selectedSpotifySong.popularity}</p>}
              {selectedSpotifySong.bpm && <p className="modal-text"><strong>BPM:</strong> {selectedSpotifySong.bpm}</p>}
              {selectedSpotifySong.energy && <p className="modal-text"><strong>Energy:</strong> {selectedSpotifySong.energy}</p>}
              {selectedSpotifySong.valence && <p className="modal-text"><strong>Valence:</strong> {selectedSpotifySong.valence}</p>}
              {selectedSpotifySong.danceability && <p className="modal-text"><strong>Danceability:</strong> {selectedSpotifySong.danceability}</p>}
              {selectedSpotifySong.decade && <p className="modal-text"><strong>Decade:</strong> {selectedSpotifySong.decade}</p>}
            </div>
            {searchError && <p className="modal-text error-text">{searchError}</p>}
            <div className="song-actions">
              <button
                onClick={() => submitSongRequest(selectedSpotifySong)}
                className="action-button"
                disabled={isSearching}
              >
                {isSearching ? "Requesting..." : "Add Request for Karaoke Version"}
              </button>
              <button
                onClick={() => {
                  setShowSpotifyDetailsModal(false);
                  setSearchError(null);
                }}
                className="action-button"
                disabled={isSearching}
              >
                Done
              </button>
            </div>
          </div>
        </div>
      )}

      {showRequestConfirmationModal && requestedSong && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h3 className="modal-title">Request Submitted</h3>
            <p className="modal-text">
              A request has been made on your behalf to find a Karaoke version of '{requestedSong.title}' by {requestedSong.artist}.
            </p>
            <button onClick={resetSearch} className="modal-cancel">Done</button>
          </div>
        </div>
      )}

      {showReorderErrorModal && reorderError && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h3 className="modal-title">Reorder Failed</h3>
            <p className="modal-text error-text">{reorderError}</p>
            <button onClick={() => setShowReorderErrorModal(false)} className="modal-cancel">Close</button>
          </div>
        </div>
      )}

      {selectedSong && (
        <SongDetailsModal
          song={selectedSong}
          isFavorite={favorites.some((fav: Song) => fav.id === selectedSong.id)}
          isInQueue={currentEvent ? (myQueues[currentEvent.eventId]?.some((q: EventQueueItem) => q.songId === selectedSong.id) || false) : false}
          onClose={() => {
            setSelectedSong(null);
            setSelectedQueueId(undefined);
          }}
          onToggleFavorite={isSingerOnly ? undefined : toggleFavorite}
          onAddToQueue={isSingerOnly ? undefined : addToEventQueue}
          onDeleteFromQueue={isSingerOnly ? undefined : (currentEvent && selectedQueueId ? handleDeleteSong : undefined)}
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