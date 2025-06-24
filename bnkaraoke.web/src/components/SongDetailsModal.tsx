import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import './SongDetailsModal.css';
import { Song, Event, AttendanceAction } from '../types';
import { API_ROUTES } from '../config/apiConfig';
import useEventContext from '../context/EventContext';

// Permanent fix for ESLint warnings (May 2025)
interface SongDetailsModalProps {
  song: Song;
  isFavorite: boolean;
  isInQueue: boolean;
  onClose: () => void;
  onToggleFavorite?: (song: Song) => Promise<void>;
  onAddToQueue?: (song: Song, eventId: number) => Promise<void>;
  onDeleteFromQueue?: (eventId: number, queueId: number) => Promise<void>;
  eventId?: number;
  queueId?: number;
  readOnly?: boolean;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
}

const SongDetailsModal: React.FC<SongDetailsModalProps> = ({
  song,
  isFavorite,
  isInQueue,
  onClose,
  onToggleFavorite,
  onAddToQueue,
  onDeleteFromQueue,
  eventId,
  queueId,
  readOnly = false,
  checkedIn,
  isCurrentEventLive,
}) => {
  const navigate = useNavigate();
  const { currentEvent, setCurrentEvent, setCheckedIn, setIsCurrentEventLive } = useEventContext();
  const [events, setEvents] = useState<Event[]>([]);
  const [liveEvents, setLiveEvents] = useState<Event[]>([]);
  const [isAddingToQueue, setIsAddingToQueue] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showEventSelectionModal, setShowEventSelectionModal] = useState(false);
  const [showJoinConfirmation, setShowJoinConfirmation] = useState(false);
  const [selectedEventId, setSelectedEventId] = useState<number | null>(null);

  // Fetch events for pre-selection and live event status
  useEffect(() => {
    if (readOnly || isInQueue || !onAddToQueue) return;

    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found in SongDetailsModal");
      setEvents([]);
      setLiveEvents([]);
      setError("Authentication token missing. Please log in again.");
      navigate("/");
      return;
    }

    const fetchEvents = async () => {
      try {
        console.log(`SongDetailsModal - Fetching events from: ${API_ROUTES.EVENTS}`);
        const response = await fetch(API_ROUTES.EVENTS, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) {
          const errorText = await response.text();
          throw new Error(`Fetch events failed: ${response.status} - ${errorText}`);
        }
        const data: Event[] = await response.json();
        console.log("SongDetailsModal - Fetched events:", data);

        const upcoming = data.filter(event =>
          event.status.toLowerCase() === "upcoming" &&
          event.visibility.toLowerCase() === "visible" &&
          !event.isCanceled
        );
        const live = data.filter(event =>
          event.status.toLowerCase() === "live" &&
          event.visibility.toLowerCase() === "visible" &&
          !event.isCanceled
        );
        setEvents(upcoming);
        setLiveEvents(live);
        console.log("SongDetailsModal - Upcoming events:", upcoming);
        console.log("SongDetailsModal - Live events:", live);
      } catch (err) {
        console.error("SongDetailsModal - Fetch events error:", err);
        setEvents([]);
        setLiveEvents([]);
        setError("Failed to load events. Please try again.");
      }
    };

    fetchEvents();
  }, [navigate, isInQueue, onAddToQueue, readOnly]);

  const handleAddToQueue = async (eventId: number) => {
    console.log("handleAddToQueue called with eventId:", eventId, "song:", song, "onAddToQueue:", !!onAddToQueue);
    if (!onAddToQueue) {
      console.error("onAddToQueue is not defined");
      setError("Cannot add to queue: Functionality not available.");
      return;
    }

    if (!eventId) {
      console.error("Event ID is missing");
      setError("Please select an event to add the song to the queue.");
      return;
    }

    setIsAddingToQueue(true);
    setError(null);

    try {
      await onAddToQueue(song, eventId);
      console.log("Song successfully added to queue for eventId:", eventId);
      setShowEventSelectionModal(false);
      setShowJoinConfirmation(false);
      onClose();
    } catch (err) {
      console.error("SongDetailsModal - Add to queue error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to add song to queue. Please try again.";
      setError(errorMessage);
      if (errorMessage.includes("User not found")) {
        localStorage.clear();
        navigate("/");
      }
    } finally {
      setIsAddingToQueue(false);
    }
  };

  const handleDeleteFromQueue = async () => {
    console.log("handleDeleteFromQueue called with eventId:", eventId, "queueId:", queueId, "onDeleteFromQueue:", !!onDeleteFromQueue);
    if (!onDeleteFromQueue || !eventId || !queueId) {
      console.error("Cannot delete from queue: Missing onDeleteFromQueue, eventId, or queueId");
      setError("Cannot delete from queue: Missing information.");
      return;
    }

    setIsDeleting(true);
    setError(null);

    try {
      await onDeleteFromQueue(eventId, queueId);
      console.log("Song successfully deleted from queue for eventId:", eventId, "queueId:", queueId);
      onClose();
    } catch (err) {
      console.error("SongDetailsModal - Delete from queue error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to delete song from queue. Please try again.";
      setError(errorMessage);
      if (errorMessage.includes("User not found")) {
        localStorage.clear();
        navigate("/");
      }
    } finally {
      setIsDeleting(false);
    }
  };

  const handleOpenEventSelection = () => {
    console.log("handleOpenEventSelection called");
    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("UserName not found in localStorage");
      setError("User not found. Please log in again to add songs to the queue.");
      localStorage.clear();
      navigate("/");
      return;
    }
    if (liveEvents.length > 0 && !checkedIn) {
      console.log("Live events exist, blocking event selection for non-checked-in user");
      setError("You must be checked into a live event to add to its queue.");
      return;
    }
    setShowEventSelectionModal(true);
  };

  // TODO: Implement handleJoinAndAdd or remove if not needed
  // const handleJoinAndAdd = (eventId: number) => {
  //   console.log("handleJoinAndAdd called for eventId:", eventId);
  //   setSelectedEventId(eventId);
  //   setShowJoinConfirmation(true);
  // };

  const confirmJoinAndAdd = async () => {
    if (!selectedEventId || !onAddToQueue) return;

    const token = localStorage.getItem("token");
    if (!token) {
      setError("Please log in to join the event.");
      navigate("/login");
      return;
    }

    try {
      const requestorId = localStorage.getItem("userName") || "unknown";
      const requestData: AttendanceAction = { RequestorId: requestorId };
      const response = await fetch(`${API_ROUTES.EVENTS}/${selectedEventId}/attendance/check-in`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log(`Check-in Response for event ${selectedEventId}:`, response.status, responseText);
      if (!response.ok) {
        throw new Error(`Check-in failed: ${response.status} - ${responseText}`);
      }

      const event = events.find(e => e.eventId === selectedEventId) || currentEvent;
      if (event) {
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
      }

      await handleAddToQueue(selectedEventId);
    } catch (err) {
      console.error("Join and add error:", err);
      setError(err instanceof Error ? err.message : "Failed to join event and add song.");
    }
  };

  console.log("Rendering SongDetailsModal with song:", song, "isFavorite:", isFavorite, "isInQueue:", isInQueue, "eventId:", eventId, "queueId:", queueId, "readOnly:", readOnly, "checkedIn:", checkedIn, "isCurrentEventLive:", isCurrentEventLive, "liveEvents:", liveEvents);

  return (
    <>
      <div className="modal-overlay song-details-modal">
        <div className="modal-content song-details-modal">
          <h3 className="modal-title">{song.title}</h3>
          <div className="song-details">
            <p className="modal-text"><strong>Artist:</strong> {song.artist}</p>
            {song.genre && <p className="modal-text"><strong>Genre:</strong> {song.genre}</p>}
            {song.popularity && <p className="modal-text"><strong>Popularity:</strong> {song.popularity}</p>}
            {song.bpm && <p className="modal-text"><strong>BPM:</strong> {song.bpm}</p>}
            {song.energy && <p className="modal-text"><strong>Energy:</strong> {song.energy}</p>}
            {song.valence && <p className="modal-text"><strong>Valence:</strong> {song.valence}</p>}
            {song.danceability && <p className="modal-text"><strong>Danceability:</strong> {song.danceability}</p>}
            {song.decade && <p className="modal-text"><strong>Decade:</strong> {song.decade}</p>}
          </div>
          {error && <p className="modal-error">{error}</p>}
          {!readOnly && (
            <div className="song-actions">
              {onToggleFavorite && (
                <button
                  onClick={() => {
                    console.log("Toggle favorite button clicked for song:", song);
                    onToggleFavorite(song);
                  }}
                  onTouchStart={() => {
                    console.log("Toggle favorite button touched for song:", song);
                    onToggleFavorite(song);
                  }}
                  className="action-button"
                >
                  {isFavorite ? "Remove from Favorites" : "Add to Favorites"}
                </button>
              )}
              {isInQueue && onDeleteFromQueue && eventId && queueId ? (
                <button
                  onClick={() => {
                    console.log("Remove from Queue button clicked");
                    handleDeleteFromQueue();
                  }}
                  onTouchStart={() => {
                    console.log("Remove from Queue button touched");
                    handleDeleteFromQueue();
                  }}
                  className="action-button"
                  disabled={isDeleting}
                >
                  {isDeleting ? "Deleting..." : "Remove from Queue"}
                </button>
              ) : (
                checkedIn && isCurrentEventLive && onAddToQueue && currentEvent && (
                  <button
                    onClick={() => {
                      console.log("Add to Queue button clicked with currentEvent:", currentEvent);
                      handleAddToQueue(currentEvent.eventId);
                    }}
                    onTouchStart={() => {
                      console.log("Add to Queue button touched with currentEvent:", currentEvent);
                      handleAddToQueue(currentEvent.eventId);
                    }}
                    className="action-button"
                    disabled={isAddingToQueue || isInQueue}
                  >
                    {isAddingToQueue ? "Adding..." : `Add to Queue: ${currentEvent.eventCode}`}
                  </button>
                )
              )}
              {!checkedIn && !isCurrentEventLive && liveEvents.length === 0 && onAddToQueue && (
                <button
                  onClick={() => {
                    console.log("Add to Queue (pre-select) button clicked");
                    handleOpenEventSelection();
                  }}
                  onTouchStart={() => {
                    console.log("Add to Queue (pre-select) button touched");
                    handleOpenEventSelection();
                  }}
                  className="action-button"
                  disabled={isAddingToQueue || events.length === 0 || isInQueue}
                >
                  {isAddingToQueue ? "Adding..." : "Add to Queue"}
                </button>
              )}
            </div>
          )}
          <div className="modal-footer">
            <button
              onClick={onClose}
              onTouchStart={onClose}
              className="action-button"
            >
              Done
            </button>
          </div>
        </div>
      </div>

      {showEventSelectionModal && !readOnly && (
        <div className="modal-overlay secondary-modal song-details-modal">
          <div className="modal-content song-details-modal">
            <h3 className="modal-title">Select Event Queue</h3>
            {error && <p className="modal-error">{error}</p>}
            <div className="event-list">
              {events
                .filter(event => event.status.toLowerCase() === "upcoming")
                .map(event => (
                  <div
                    key={event.eventId}
                    className="event-item"
                    onClick={() => {
                      console.log("Event selected for adding to queue:", event);
                      handleAddToQueue(event.eventId);
                    }}
                    onTouchStart={() => {
                      console.log("Event selected for adding to queue (touch):", event);
                      handleAddToQueue(event.eventId);
                    }}
                  >
                    {event.status}: {event.eventCode} ({event.scheduledDate})
                  </div>
                ))}
            </div>
            <div className="modal-footer">
              <button
                onClick={() => {
                  console.log("Cancel event selection modal");
                  setShowEventSelectionModal(false);
                }}
                onTouchStart={() => {
                  console.log("Cancel event selection modal (touch)");
                  setShowEventSelectionModal(false);
                }}
                className="action-button"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {showJoinConfirmation && selectedEventId && (
        <div className="modal-overlay secondary-modal song-details-modal">
          <div className="modal-content song-details-modal">
            <h3 className="modal-title">Join Event</h3>
            <p>Do you want to join the event and add this song to the queue?</p>
            <div className="modal-footer">
              <button
                onClick={() => {
                  console.log("Confirm join and add for eventId:", selectedEventId);
                  confirmJoinAndAdd();
                }}
                onTouchStart={() => {
                  console.log("Confirm join and add (touch) for eventId:", selectedEventId);
                  confirmJoinAndAdd();
                }}
                className="action-button"
                disabled={isAddingToQueue}
              >
                {isAddingToQueue ? "Joining..." : "Join and Add"}
              </button>
              <button
                onClick={() => {
                  console.log("Cancel join confirmation");
                  setShowJoinConfirmation(false);
                  setSelectedEventId(null);
                }}
                onTouchStart={() => {
                  console.log("Cancel join confirmation (touch)");
                  setShowJoinConfirmation(false);
                  setSelectedEventId(null);
                }}
                className="action-button"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default SongDetailsModal;