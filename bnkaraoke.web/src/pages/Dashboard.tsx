import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import './Dashboard.css';
import { API_ROUTES } from '../config/apiConfig';
import SongDetailsModal from '../components/SongDetailsModal';
import { Song, SpotifySong, EventQueueItem, EventQueueItemResponse, Event } from '../types';
import useEventContext from '../context/EventContext';
import { DndContext, closestCenter, KeyboardSensor, PointerSensor, useSensor, useSensors, DragEndEvent } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy, useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';

// Permanent fix for ESLint warnings (May 2025)
interface SortableQueueItemProps {
  queueItem: EventQueueItem;
  eventId: number;
  songDetails: Song | null;
  onClick: (song: Song, queueId: number, eventId: number) => void;
}

const SortableQueueItem: React.FC<SortableQueueItemProps> = ({ queueItem, eventId, songDetails, onClick }) => {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: queueItem.queueId });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className="queue-song"
      onClick={() => {
        console.log("SortableQueueItem clicked with songDetails:", songDetails, "queueId:", queueItem.queueId, "eventId:", eventId);
        songDetails && onClick(songDetails, queueItem.queueId, eventId);
      }}
      onTouchStart={() => {
        console.log("SortableQueueItem touched with songDetails:", songDetails, "queueId:", queueItem.queueId, "eventId:", eventId);
        songDetails && onClick(songDetails, queueItem.queueId, eventId);
      }}
    >
      <span>
        {songDetails ? (
          `${songDetails.title} - ${songDetails.artist}`
        ) : (
          `Loading Song ${queueItem.songId}...`
        )}
      </span>
    </div>
  );
};

const Dashboard: React.FC = () => {
  const navigate = useNavigate();
  const { currentEvent, checkedIn, isCurrentEventLive } = useEventContext();
  const [myQueues, setMyQueues] = useState<{ [eventId: number]: EventQueueItem[] }>({});
  const [globalQueue, setGlobalQueue] = useState<EventQueueItem[]>([]);
  const [songDetailsMap, setSongDetailsMap] = useState<{ [songId: number]: Song }>({});
  const [favorites, setFavorites] = useState<Song[]>([]);
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [songs, setSongs] = useState<Song[]>([]);
  const [spotifySongs, setSpotifySongs] = useState<SpotifySong[]>([]);
  const [selectedSpotifySong, setSelectedSpotifySong] = useState<SpotifySong | null>(null);
  const [showSpotifyModal, setShowSpotifyModal] = useState<boolean>(false);
  const [showSpotifyDetailsModal, setShowSpotifyDetailsModal] = useState<boolean>(false);
  const [showRequestConfirmationModal, setShowRequestConfirmationModal] = useState<boolean>(false);
  const [requestedSong, setRequestedSong] = useState<SpotifySong | null>(null);
  const [selectedSong, setSelectedSong] = useState<Song | null>(null);
  const [selectedQueueId, setSelectedQueueId] = useState<number | undefined>(undefined);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState<boolean>(false);
  const [showSearchModal, setShowSearchModal] = useState<boolean>(false);
  const [reorderError, setReorderError] = useState<string | null>(null);
  const [showReorderErrorModal, setShowReorderErrorModal] = useState<boolean>(false);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [isSingerOnly, setIsSingerOnly] = useState<boolean>(false);

  // Check user roles
  useEffect(() => {
    const roles = JSON.parse(localStorage.getItem("roles") || "[]") as string[];
    setIsSingerOnly(roles.length === 1 && roles.includes("Singer"));
  }, []);

  // Fetch queue on mount to sync with backend
  useEffect(() => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    console.log("Fetching queues on mount - token:", token, "userName:", userName);
    if (!token || !userName) {
      console.error("No token or userName found on mount");
      setFetchError("Please log in to view your queue.");
      return;
    }

    const fetchAllQueues = async () => {
      try {
        const eventsResponse = await fetch(API_ROUTES.EVENTS, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!eventsResponse.ok) {
          const errorText = await eventsResponse.text();
          console.error(`Fetch events failed: ${eventsResponse.status} - ${errorText}`);
          if (eventsResponse.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch events failed: ${eventsResponse.status}`);
        }
        const eventsData: Event[] = await eventsResponse.json();
        console.log("Fetched events on mount:", eventsData);

        const newQueues: { [eventId: number]: EventQueueItem[] } = {};
        for (const event of eventsData) {
          try {
            const queueResponse = await fetch(`${API_ROUTES.EVENT_QUEUE}/${event.eventId}/queue`, {
              headers: { Authorization: `Bearer ${token}` },
            });
            if (!queueResponse.ok) {
              const errorText = await queueResponse.text();
              console.error(`Fetch queue failed for event ${event.eventId}: ${queueResponse.status} - ${errorText}`);
              if (queueResponse.status === 401) {
                setFetchError("Session expired. Please log in again.");
                localStorage.removeItem("token");
                navigate("/login");
                return;
              }
              throw new Error(`Fetch queue failed for event ${event.eventId}: ${queueResponse.status}`);
            }
            const queueData: EventQueueItemResponse[] = await queueResponse.json();
            const parsedQueueData: EventQueueItem[] = queueData.map(item => ({
              ...item,
              singers: item.singers ? JSON.parse(item.singers) : [],
            }));

            // Deduplicate queue items by songId and requestorUserName
            const uniqueQueueData = Array.from(
              new Map(
                parsedQueueData.map(item => [`${item.songId}-${item.requestorUserName}`, item])
              ).values()
            );
            const userQueue = uniqueQueueData.filter(item => item.requestorUserName === userName);
            console.log(`Fetched queue for event ${event.eventId} - total items: ${uniqueQueueData.length}, user items: ${userQueue.length}, userName: ${userName}`, userQueue);
            newQueues[event.eventId] = userQueue;

            const songDetails: { [songId: number]: Song } = {};
            for (const item of uniqueQueueData) {
              if (!songDetails[item.songId]) {
                try {
                  const songResponse = await fetch(`${API_ROUTES.SONG_BY_ID}/${item.songId}`, {
                    headers: { Authorization: `Bearer ${token}` },
                  });
                  if (!songResponse.ok) {
                    const errorText = await songResponse.text();
                    console.error(`Fetch song details failed for song ${item.songId}: ${songResponse.status} - ${errorText}`);
                    if (songResponse.status === 401) {
                      setFetchError("Session expired. Please log in again.");
                      localStorage.removeItem("token");
                      navigate("/login");
                      return;
                    }
                    throw new Error(`Fetch song details failed for song ${item.songId}: ${songResponse.status}`);
                  }
                  const songData = await songResponse.json();
                  songDetails[item.songId] = songData;
                } catch (err) {
                  console.error(`Error fetching song details for song ${item.songId}:`, err);
                  songDetails[item.songId] = {
                    id: item.songId,
                    title: `Song ${item.songId}`,
                    artist: 'Unknown',
                    status: 'unknown',
                    bpm: 0,
                    danceability: 0,
                    energy: 0,
                    valence: undefined,
                    popularity: 0,
                    genre: undefined,
                    decade: undefined,
                    requestDate: '',
                    requestedBy: '',
                    spotifyId: undefined,
                    youTubeUrl: undefined,
                    approvedBy: undefined,
                    musicBrainzId: undefined,
                    mood: undefined,
                    lastFmPlaycount: undefined
                  };
                }
              }
            }
            setSongDetailsMap(prev => ({ ...prev, ...songDetails }));
          } catch (err) {
            console.error(`Fetch queue error for event ${event.eventId}:`, err);
            newQueues[event.eventId] = [];
          }
        }

        setMyQueues(newQueues);
        setFetchError(null);
      } catch (err) {
        console.error("Fetch events error on mount:", err);
        setFetchError("Failed to load events. Please try again or contact support.");
      }
    };

    fetchAllQueues();
  }, [navigate]);

  // Fetch queues and song details when currentEvent changes or queue updates
  const fetchQueue = useCallback(async () => {
    if (!currentEvent) {
      setGlobalQueue([]);
      return;
    }

    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    console.log("Fetching queue for current event - eventId:", currentEvent.eventId, "token:", token, "userName:", userName);
    if (!token || !userName) {
      console.error("No token or userName found");
      setGlobalQueue([]);
      setSongDetailsMap({});
      setFetchError("Please log in to view the event queue.");
      return;
    }

    try {
      const queueResponse = await fetch(`${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!queueResponse.ok) {
        const errorText = await queueResponse.text();
        console.error(`Fetch queue failed for event ${currentEvent.eventId}: ${queueResponse.status} - ${errorText}`);
        if (queueResponse.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Fetch queue failed for event ${currentEvent.eventId}: ${queueResponse.status}`);
      }
      const data: EventQueueItemResponse[] = await queueResponse.json();
      const parsedData: EventQueueItem[] = data.map(item => ({
        ...item,
        singers: item.singers ? JSON.parse(item.singers) : [],
      }));

      // Deduplicate queue items by songId and requestorUserName
      const uniqueQueueData = Array.from(
        new Map(
          parsedData.map(item => [`${item.songId}-${item.requestorUserName}`, item])
        ).values()
      );
      const userQueue = uniqueQueueData.filter(item => item.requestorUserName === userName);
      console.log(`Fetched queue for event ${currentEvent.eventId} - total items: ${uniqueQueueData.length}, user items: ${userQueue.length}, userName: ${userName}`, userQueue);

      setMyQueues(prev => ({
        ...prev,
        [currentEvent.eventId]: userQueue,
      }));

      // Show Karaoke DJ Queue for live events when checked in
      if (checkedIn && currentEvent.status.toLowerCase() === "live") {
        setGlobalQueue(uniqueQueueData);
      } else {
        setGlobalQueue([]);
      }

      const songDetails: { [songId: number]: Song } = {};
      for (const item of uniqueQueueData) {
        if (!songDetails[item.songId]) {
          try {
            const songResponse = await fetch(`${API_ROUTES.SONG_BY_ID}/${item.songId}`, {
              headers: { Authorization: `Bearer ${token}` },
            });
            if (!songResponse.ok) {
              const errorText = await songResponse.text();
              console.error(`Fetch song details failed for song ${item.songId}: ${songResponse.status} - ${errorText}`);
              if (songResponse.status === 401) {
                setFetchError("Session expired. Please log in again.");
                localStorage.removeItem("token");
                navigate("//login");
                return;
              }
              throw new Error(`Fetch song details failed for song ${item.songId}: ${songResponse.status}`);
            }
            const songData = await songResponse.json();
            songDetails[item.songId] = songData;
          } catch (err) {
            console.error(`Error fetching song details for song ${item.songId}:`, err);
            songDetails[item.songId] = {
              id: item.songId,
              title: `Song ${item.songId}`,
              artist: 'Unknown',
              status: 'unknown',
              bpm: 0,
              danceability: 0,
              energy: 0,
              valence: undefined,
              popularity: 0,
              genre: undefined,
              decade: undefined,
              requestDate: '',
              requestedBy: '',
              spotifyId: undefined,
              youTubeUrl: undefined,
              approvedBy: undefined,
              musicBrainzId: undefined,
              mood: undefined,
              lastFmPlaycount: undefined
            };
          }
        }
      }
      setSongDetailsMap(prev => ({ ...prev, ...songDetails }));
      setFetchError(null);
    } catch (err) {
      console.error(`Fetch queue error for event ${currentEvent.eventId}:`, err);
      setGlobalQueue([]);
      setSongDetailsMap({});
      setFetchError("Failed to load the event queue. Please try again or contact support.");
    }
  }, [currentEvent, checkedIn, navigate]);

  useEffect(() => {
    fetchQueue();
  }, [currentEvent, checkedIn, isCurrentEventLive, fetchQueue]);

  const fetchSongs = async () => {
    if (!searchQuery.trim()) {
      console.log("Search query is empty, resetting songs");
      setSongs([]);
      setShowSearchModal(false);
      setSearchError(null);
      return;
    }
    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found");
      setSearchError("Authentication token missing. Please log in again.");
      setShowSearchModal(true);
      return;
    }
    setIsSearching(true);
    setSearchError(null);
    console.log(`Fetching songs with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SONGS_SEARCH}?query=${encodeURIComponent(searchQuery)}&page=1&pageSize=50`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`Fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Search failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log("Fetch response:", data);
      const fetchedSongs = (data.songs as Song[]) || [];
      console.log("Fetched songs:", fetchedSongs);
      const activeSongs = fetchedSongs.filter(song => song.status && song.status.toLowerCase() === "active");
      console.log("Filtered active songs:", activeSongs);
      setSongs(activeSongs);

      if (activeSongs.length === 0) {
        setSearchError("There are no Karaoke songs available that match your search terms. Would you like to request a Karaoke song be added?");
        setShowSearchModal(true);
      } else {
        setShowSearchModal(true);
      }
      setIsSearching(false);
    } catch (err) {
      console.error("Search error:", err);
      setSearchError(err instanceof Error ? err.message : "An unknown error occurred while searching.");
      setSongs([]);
      setShowSearchModal(true);
      setIsSearching(false);
    }
  };

  const fetchSpotifySongs = async () => {
    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found");
      setSearchError("Please log in again to search songs.");
      return;
    }
    console.log(`Fetching songs from Spotify with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`Spotify fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Spotify search failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log("Spotify fetch response:", data);
      const fetchedSpotifySongs = (data.songs as SpotifySong[]) || [];
      console.log("Fetched Spotify songs:", fetchedSpotifySongs);
      setSpotifySongs(fetchedSpotifySongs);
      setShowSpotifyModal(true);
      setShowSearchModal(false);
    } catch (err) {
      console.error("Spotify search error:", err);
      setSearchError(err instanceof Error ? err.message : "An unknown error occurred while searching Spotify.");
      setShowSearchModal(true);
    }
  };

  const handleSpotifySongSelect = (song: SpotifySong) => {
    console.log("handleSpotifySongSelect called with song:", song);
    setSelectedSpotifySong(song);
    setShowSpotifyDetailsModal(true);
  };

  const submitSongRequest = async (song: SpotifySong) => {
    console.log("Submitting song request:", song);
    const token = localStorage.getItem("token");
    const userId = localStorage.getItem("userName") || "Unknown User";
    if (!token) {
      console.error("No token found");
      setSearchError("Please log in again to request a song.");
      return;
    }
    if (!userId || userId === "Unknown User") {
      console.error("No userName found in localStorage");
      setSearchError("User ID missing. Please log in again.");
      return;
    }

    const requestData = {
      title: song.title || "Unknown Title",
      artist: song.artist || "Unknown Artist",
      spotifyId: song.id,
      bpm: song.bpm || 0,
      danceability: song.danceability || 0,
      energy: song.energy || 0,
      valence: song.valence || null,
      popularity: song.popularity || 0,
      genre: song.genre || null,
      decade: song.decade || null,
      status: "pending",
      requestedBy: userId
    };

    console.log("Sending song request payload:", requestData);

    try {
      setIsSearching(true);
      const response = await fetch(API_ROUTES.REQUEST_SONG, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${token}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log(`Song request response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`Failed to submit song request: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Song request failed: ${response.status} - ${responseText}`);
      }

      let result = {};
      if (responseText) {
        try {
          result = JSON.parse(responseText);
        } catch (error) {
          console.error("Failed to parse response as JSON:", responseText);
          throw new Error("Invalid response format from server");
        }
      }

      console.log("Parsed response:", result);
      console.log("Setting state: closing Spotify modal, opening confirmation");
      setRequestedSong(song);
      setShowSpotifyDetailsModal(false);
      setShowRequestConfirmationModal(true);
    } catch (err) {
      console.error("Song request error:", err);
      setSearchError(err instanceof Error ? err.message : "Failed to submit song request.");
    } finally {
      setIsSearching(false);
    }
  };

  const handleSearchClick = () => {
    console.log("handleSearchClick called");
    fetchSongs();
  };

  const handleSearchKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      console.log("handleSearchKeyDown - Enter key pressed");
      fetchSongs();
    }
  };

  const resetSearch = () => {
    console.log("resetSearch called");
    setSearchQuery("");
    setSongs([]);
    setSpotifySongs([]);
    setSelectedSpotifySong(null);
    setShowSearchModal(false);
    setShowSpotifyModal(false);
    setShowSpotifyDetailsModal(false);
    setShowRequestConfirmationModal(false);
    setRequestedSong(null);
    setSelectedSong(null);
    setSelectedQueueId(undefined);
    setSearchError(null);
    setReorderError(null);
    setShowReorderErrorModal(false);
  };

  const toggleFavorite = async (song: Song) => {
    console.log("toggleFavorite called with song:", song);
    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found in toggleFavorite");
      setFetchError("Please log in to manage favorites.");
      return;
    }

    const isFavorite = favorites.some(fav => fav.id === song.id);
    const method = isFavorite ? 'DELETE' : 'POST';
    const url = isFavorite ? `${API_ROUTES.FAVORITES}/${song.id}` : API_ROUTES.FAVORITES;

    console.log(`Toggling favorite for song ${song.id}, isFavorite: ${isFavorite}, method: ${method}, url: ${url}`);

    try {
      const response = await fetch(url, {
        method,
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: method === 'POST' ? JSON.stringify({ songId: song.id }) : undefined,
      });

      const responseText = await response.text();
      console.log(`Toggle favorite response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`Failed to ${isFavorite ? 'remove' : 'add'} favorite: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`${isFavorite ? 'Remove' : 'Add'} favorite failed: ${response.status}`);
      }

      let result;
      try {
        result = JSON.parse(responseText);
      } catch (error) {
        console.error("Failed to parse response as JSON:", responseText);
        throw new Error("Invalid response format from server");
      }

      console.log(`Parsed toggle favorite response:`, result);

      if (result.success) {
        const updatedFavorites = isFavorite
          ? favorites.filter(fav => fav.id !== song.id)
          : [...favorites, { ...song }];
        console.log(`Updated favorites after ${isFavorite ? 'removal' : 'addition'}:`, updatedFavorites);
        setFavorites([...updatedFavorites]);
      } else {
        console.error("Toggle favorite failed: Success flag not set in response");
      }
    } catch (err) {
      console.error(`${isFavorite ? 'Remove' : 'Add'} favorite error:`, err);
      setFetchError(`Failed to ${isFavorite ? 'remove' : 'add'} favorite. Please try again.`);
    }
  };

  const addToEventQueue = async (song: Song, eventId: number) => {
    const token = localStorage.getItem("token");
    const requestorUserName = localStorage.getItem("userName");
    console.log("addToEventQueue called with song:", song, "eventId:", eventId, "token:", token, "requestorUserName:", requestorUserName);

    if (!token) {
      console.error("No token found in addToEventQueue");
      setFetchError("Authentication token missing. Please log in again.");
      return;
    }

    if (!requestorUserName) {
      console.error("Invalid or missing requestorUserName in addToEventQueue");
      setFetchError("User not found. Please log in again to add songs to the queue.");
      return;
    }

    const queueForEvent = myQueues[eventId] || [];
    const isInQueue = queueForEvent.some(q => q.songId === song.id);
    if (isInQueue) {
      console.log(`Song ${song.id} is already in the queue for event ${eventId}`);
      return;
    }

    try {
      const requestBody = JSON.stringify({
        songId: song.id,
        requestorUserName: requestorUserName,
      });
      console.log("addToEventQueue - Sending request to:", `${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, "with body:", requestBody);

      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: requestBody,
      });

      const responseText = await response.text();
      console.log(`Add to queue response for event ${eventId}: status=${response.status}, body=${responseText}`);

      if (!response.ok) {
        console.error(`Failed to add song to queue for event ${eventId}: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Failed to add song: ${responseText || response.statusText}`);
      }

      const newQueueItem: EventQueueItemResponse = JSON.parse(responseText);
      const parsedQueueItem: EventQueueItem = {
        ...newQueueItem,
        singers: newQueueItem.singers ? JSON.parse(newQueueItem.singers) : [],
      };
      console.log(`Added to queue for event ${eventId}:`, parsedQueueItem);

      // Re-fetch queue to update Karaoke DJ Queue
      await fetchQueue();
    } catch (err) {
      console.error("Add to queue error:", err);
      setFetchError("Failed to add song to queue. Please try again.");
    }
  };

  const handleDeleteSong = async (eventId: number, queueId: number) => {
    console.log("handleDeleteSong called with eventId:", eventId, "queueId:", queueId);
    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found");
      setFetchError("Please log in to manage the queue.");
      return;
    }

    try {
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue/${queueId}/skip`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error(`Skip song failed: ${response.status} - ${errorText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Skip song failed: ${response.status}`);
      }

      // Re-fetch queue to update Karaoke DJ Queue
      await fetchQueue();
    } catch (err) {
      console.error("Skip song error:", err);
      setFetchError("Failed to remove song from queue. Please try again.");
      setMyQueues(prev => ({
        ...prev,
        [eventId]: (prev[eventId] || []).filter(q => q.queueId !== queueId),
      }));
      setGlobalQueue(prev => prev.filter(q => q.queueId !== queueId));
    }
  };

  const handleQueueItemClick = (song: Song, queueId: number, eventId: number) => {
    console.log("handleQueueItemClick called with song:", song, "queueId:", queueId, "eventId:", eventId);
    setSelectedSong(song);
    setSelectedQueueId(queueId);
  };

  const handleGlobalQueueItemClick = (song: Song) => {
    console.log("handleGlobalQueueItemClick called with song:", song);
    setSelectedSong(song);
  };

  const handleDragEnd = async (event: DragEndEvent) => {
    console.log("handleDragEnd called with event:", event);
    const { active, over } = event;

    if (!active || !over || active.id === over.id) {
      console.log("Drag-and-drop: No action needed - same position or invalid drag");
      return;
    }

    if (!currentEvent) {
      console.error("Drag-and-drop: No current event selected");
      setReorderError("No event selected. Please select an event and try again.");
      setShowReorderErrorModal(true);
      return;
    }

    const currentQueue = myQueues[currentEvent.eventId] || [];
    console.log("Drag-and-drop: Current queue before reorder:", currentQueue);

    const oldIndex = currentQueue.findIndex(item => item.queueId === active.id);
    const newIndex = currentQueue.findIndex(item => item.queueId === over.id);
    console.log(`Drag-and-drop: Moving item from index ${oldIndex} to ${newIndex}`);

    const newQueue = [...currentQueue];
    const [movedItem] = newQueue.splice(oldIndex, 1);
    newQueue.splice(newIndex, 0, movedItem);

    const updatedQueue = newQueue.map((item, index) => ({
      ...item,
      position: index + 1,
    }));

    console.log("Drag-and-drop: Updated queue with new positions:", updatedQueue);

    setMyQueues(prev => ({
      ...prev,
      [currentEvent.eventId]: updatedQueue,
    }));

    const newOrder = updatedQueue.map(item => ({
      QueueId: item.queueId,
      Position: item.position,
    }));

    console.log("Drag-and-drop: Sending new order to backend:", newOrder);

    const token = localStorage.getItem("token");
    if (!token) {
      console.error("Drag-and-drop: No token");
      setReorderError("Authentication token missing. Please log in again.");
      setShowReorderErrorModal(true);
      return;
    }

    try {
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue/reorder`, {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ NewOrder: newOrder }),
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error(`Drag-and-drop: Reorder failed: ${response.status} - ${errorText}`);
        if (response.status === 401) {
          setReorderError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          navigate("/login");
          return;
        }
        throw new Error(`Reorder failed: ${response.status} - ${errorText}`);
      }

      // Re-fetch queue to update Karaoke DJ Queue
      await fetchQueue();
      setReorderError(null);
      setShowReorderErrorModal(false);
    } catch (err) {
      console.error("Drag-and-drop: Reorder error:", err);
      setReorderError("Failed to reorder queue. Please try again or contact support.");
      setShowReorderErrorModal(true);
      setMyQueues(prev => ({
        ...prev,
        [currentEvent.eventId]: currentQueue,
      }));
    }
  };

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  useEffect(() => {
    const token = localStorage.getItem("token");
    if (!token) {
      console.error("No token found");
      setFavorites([]);
      setFetchError("Please log in to view favorites.");
      return;
    }
    console.log("Fetching favorites from:", API_ROUTES.FAVORITES);
    fetch(`${API_ROUTES.FAVORITES}`, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then(res => {
        if (!res.ok) {
          console.error(`Fetch favorites failed with status: ${res.status}`);
          if (res.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch favorites failed: ${res.status}`);
        }
        return res.json();
      })
      .then((data: Song[]) => {
        console.log("Fetched favorites:", data);
        setFavorites(data || []);
        setFetchError(null);
      })
      .catch(err => {
        console.error("Fetch favorites error:", err);
        setFavorites([]);
        setFetchError("Failed to load favorites. Please try again or contact support.");
      });
  }, [navigate]);

  return (
    <div className="dashboard">
      <div className="dashboard-content">
        {fetchError && <p className="error-text">{fetchError}</p>}
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
              ▶
            </button>
            <button
              onClick={resetSearch}
              onTouchStart={resetSearch}
              className="reset-button"
              aria-label="Reset search"
            >
              ■
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

        <div className="main-content">
          {currentEvent !== null && (checkedIn || !isCurrentEventLive) && (
            <aside className="queue-panel">
              <h2>My Song Queue</h2>
              {reorderError && !showReorderErrorModal && <p className="error-text">{reorderError}</p>}
              {!currentEvent ? (
                <p>Please select an event to view your queue.</p>
              ) : !myQueues[currentEvent.eventId] || myQueues[currentEvent.eventId].length === 0 ? (
                <p>No songs in your queue for this event.</p>
              ) : (
                <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                  <SortableContext items={myQueues[currentEvent.eventId].map(item => item.queueId)} strategy={verticalListSortingStrategy}>
                    <div className="event-queue">
                      <h3>{currentEvent.description}</h3>
                      <p className="queue-info">{myQueues[currentEvent.eventId].length}/{currentEvent.requestLimit} songs</p>
                      {myQueues[currentEvent.eventId].map(queueItem => (
                        <SortableQueueItem
                          key={queueItem.queueId}
                          queueItem={queueItem}
                          eventId={currentEvent.eventId}
                          songDetails={songDetailsMap[queueItem.songId] || null}
                          onClick={handleQueueItemClick}
                        />
                      ))}
                    </div>
                  </SortableContext>
                </DndContext>
              )}
            </aside>
          )}

          {checkedIn && isCurrentEventLive && currentEvent && currentEvent.status.toLowerCase() === "live" && (
            <aside className="global-queue-panel">
              <h2>Karaoke DJ Queue</h2>
              <p className="queue-info">Total Songs: {globalQueue.length}</p>
              {globalQueue.length === 0 ? (
                <p>No songs in the Karaoke DJ Queue.</p>
              ) : (
                <div className="event-queue">
                  <h3>{currentEvent.description}</h3>
                  <p className="queue-info">{myQueues[currentEvent.eventId]?.length || 0}/{currentEvent.requestLimit} songs</p>
                  {globalQueue
                    .sort((a, b) => a.position - b.position)
                    .map(queueItem => (
                      <div
                        key={queueItem.queueId}
                        className="queue-song"
                        onClick={() => {
                          const song = songDetailsMap[queueItem.songId];
                          if (song) handleGlobalQueueItemClick(song);
                        }}
                        onTouchStart={() => {
                          const song = songDetailsMap[queueItem.songId];
                          if (song) handleGlobalQueueItemClick(song);
                        }}
                      >
                        <span>
                          {songDetailsMap[queueItem.songId] ? (
                            `${queueItem.position}. ${songDetailsMap[queueItem.songId].title} - ${songDetailsMap[queueItem.songId].artist}`
                          ) : (
                            `Loading Song ${queueItem.songId}...`
                          )}
                        </span>
                      </div>
                    ))}
                </div>
              )}
            </aside>
          )}

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
                      console.log("Favorite song clicked to open SongDetailsModal with song:", song);
                      setSelectedSong(song);
                    }}
                  >
                    <span>{song.title} - {song.artist}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </div>

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
            {/* eslint-disable-next-line react/no-unescaped-entities */}
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
          isFavorite={favorites.some(fav => fav.id === selectedSong.id)}
          isInQueue={currentEvent ? (myQueues[currentEvent.eventId]?.some(q => q.songId === selectedSong.id) || false) : false}
          onClose={() => {
            setSelectedSong(null);
            setSelectedQueueId(undefined);
          }}
          onToggleFavorite={isSingerOnly ? () => Promise.resolve() : toggleFavorite}
          onAddToQueue={isSingerOnly ? undefined : addToEventQueue}
          onDeleteFromQueue={isSingerOnly ? undefined : (currentEvent && selectedQueueId ? handleDeleteSong : undefined)}
          eventId={currentEvent?.eventId}
          queueId={selectedQueueId}
          readOnly={isSingerOnly}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
        />
      )}
    </div>
  );
};

export default Dashboard;