import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, NavigateFunction } from 'react-router-dom';
import { DragEndEvent } from '@dnd-kit/core';
import toast, { Toaster } from 'react-hot-toast';
import './Dashboard.css';
import { API_ROUTES } from '../config/apiConfig';
import { Song, SpotifySong, EventQueueItem, EventQueueItemResponse, Event } from '../types';
import useEventContext from '../context/EventContext';
import SearchBar from '../components/SearchBar';
import QueuePanel from '../components/QueuePanel';
import GlobalQueuePanel from '../components/GlobalQueuePanel';
import FavoritesSection from '../components/FavoritesSection';
import Modals from '../components/Modals';
import useSignalR from '../hooks/useSignalR';

const API_BASE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com' : 'http://localhost:7290';

// Debounce utility
const debounce = <F extends (...args: any[]) => Promise<void>>(func: F, wait: number) => {
  let timeout: NodeJS.Timeout | null = null;
  return (...args: Parameters<F>): Promise<void> => {
    return new Promise((resolve) => {
      if (timeout) {
        clearTimeout(timeout);
      }
      timeout = setTimeout(async () => {
        timeout = null;
        await func(...args);
        resolve();
      }, wait);
    });
  };
};

const Dashboard: React.FC = () => {
  const navigate: NavigateFunction = useNavigate();
  const { currentEvent, setCurrentEvent, checkedIn, setCheckedIn, isCurrentEventLive, setIsCurrentEventLive } = useEventContext();
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
  const [hasAttemptedCheckIn, setHasAttemptedCheckIn] = useState<boolean>(false);
  const [queueMessage, setQueueMessage] = useState<string | null>(null);
  const [serverAvailable, setServerAvailable] = useState<boolean>(true);

  // Log initial state
  useEffect(() => {
    console.log("[DASHBOARD_INIT] Initial state:", { currentEvent: currentEvent ? { eventId: currentEvent.eventId, status: currentEvent.status } : null, checkedIn, isCurrentEventLive });
  }, [checkedIn, currentEvent, isCurrentEventLive]);

  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const setCurrentEventSuppressWarning = setCurrentEvent; // Suppress unused warning; may be used in future logic

  // Check server health
  const checkServerHealth = async (): Promise<boolean> => {
    try {
      const token = await validateToken();
      if (!token) {
        console.error("[DASHBOARD] No valid token for server health check");
        throw new Error("No valid token");
      }
      console.log("[DASHBOARD] Checking server health at:", `${API_BASE_URL}/api/events`);
      const response = await fetch(`${API_BASE_URL}/api/events`, {
        method: 'GET',
        headers: { Authorization: `Bearer ${token}` },
      });
      console.log("[DASHBOARD] Server health response:", { status: response.status });
      if (!response.ok) {
        throw new Error(`Server health check failed: ${response.status}`);
      }
      setServerAvailable(true);
      return true;
    } catch (err) {
      console.error("[DASHBOARD] Server health check error:", err);
      setServerAvailable(false);
      setFetchError("Unable to connect to the server. Please check if the server is running or log in again.");
      return false;
    }
  };

  // Enhanced token validation with detailed logging
  const validateToken = useCallback(async (): Promise<string | null> => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[VALIDATE_TOKEN] No token or userName found", { token: !!token, userName: !!userName });
      setFetchError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      console.log("[VALIDATE_TOKEN] Token length:", token.length);
      if (token.split('.').length !== 3) {
        console.error("[VALIDATE_TOKEN] Malformed token: does not contain three parts", { parts: token.split('.') });
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFetchError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      console.log("[VALIDATE_TOKEN] Decoded payload:", payload);
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[VALIDATE_TOKEN] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFetchError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[VALIDATE_TOKEN] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[VALIDATE_TOKEN] Error:", err, { token });
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setFetchError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  }, [navigate]);

  // Fetch queue with retry logic and debounce
  const fetchQueue = useCallback(
    debounce(async () => {
      console.log("[FETCH_QUEUE] Attempting to fetch queue", { currentEvent: currentEvent ? { eventId: currentEvent.eventId, status: currentEvent.status } : null, checkedIn, isCurrentEventLive });
      if (!currentEvent) {
        console.log("[FETCH_QUEUE] No current event, clearing queue");
        setGlobalQueue([]);
        setQueueMessage("No event selected. Please select or join an event.");
        setServerAvailable(true);
        return;
      }

      const isServerHealthy = await checkServerHealth();
      if (!isServerHealthy) {
        console.error("[FETCH_QUEUE] Server health check failed, aborting fetch");
        return;
      }

      const token = await validateToken();
      if (!token) return;

      const userName = localStorage.getItem("userName");
      console.log("[FETCH_QUEUE] Fetching queue for event:", { eventId: currentEvent.eventId, token: token.slice(0, 10), userName });
      if (!userName) {
        console.error("[FETCH_QUEUE] No userName found");
        setGlobalQueue([]);
        setSongDetailsMap({});
        setFetchError("Please log in to view the event queue.");
        setQueueMessage("Please log in to view the event queue.");
        setServerAvailable(true);
        return;
      }

      let retryCount = 0;
      const maxQueueRetries = 3;

      const attemptFetchQueue = async () => {
        try {
          console.log(`[FETCH_QUEUE] Sending request to: ${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue`);
          const queueResponse = await fetch(`${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue`, {
            headers: { Authorization: `Bearer ${token}` },
          });
          console.log(`[FETCH_QUEUE] Queue response status for event ${currentEvent.eventId}: ${queueResponse.status}`);
          if (!queueResponse.ok) {
            const errorText = await queueResponse.text();
            console.error(`[FETCH_QUEUE] Fetch queue failed for event ${currentEvent.eventId}: ${queueResponse.status} - ${errorText}`);
            if (queueResponse.status === 401) {
              setFetchError("Session expired. Please log in again.");
              localStorage.removeItem("token");
              localStorage.removeItem("userName");
              navigate("/login");
              return;
            }
            throw new Error(`Fetch queue failed for event ${currentEvent.eventId}: ${queueResponse.status} - ${errorText}`);
          }

          const rawQueueText = await queueResponse.text();
          console.log(`[FETCH_QUEUE] Raw queue response for event ${currentEvent.eventId}:`, rawQueueText);

          let queueData: EventQueueItemResponse[];
          try {
            queueData = JSON.parse(rawQueueText);
          } catch (jsonError) {
            console.error(`[FETCH_QUEUE] JSON parse error for event ${currentEvent.eventId}:`, jsonError, `Raw response:`, rawQueueText);
            throw new Error(`Failed to parse queue data for event ${currentEvent.eventId}.`);
          }

          if (!Array.isArray(queueData)) {
            console.error(`[FETCH_QUEUE] Invalid queue data for event ${currentEvent.eventId}:`, queueData);
            throw new Error(`Invalid queue data format for event ${currentEvent.eventId}.`);
          }

          console.log(`[FETCH_QUEUE] Queue data length: ${queueData.length}`);
          if (queueData.length === 0) {
            console.log(`[FETCH_QUEUE] No queue items found for event ${currentEvent.eventId}`);
            setQueueMessage("No songs in the queue for this event.");
          } else {
            setQueueMessage(null);
          }

          const parsedData: EventQueueItem[] = queueData.map(item => ({
            queueId: item.queueId,
            eventId: item.eventId,
            songId: item.songId,
            requestorUserName: item.requestorUserName,
            singers: typeof item.singers === 'string' ? JSON.parse(item.singers || '[]') : item.singers || [],
            position: item.position,
            status: item.status,
            isActive: item.isActive,
            wasSkipped: item.wasSkipped,
            isCurrentlyPlaying: item.isCurrentlyPlaying,
            sungAt: item.sungAt,
            isOnBreak: item.isOnBreak,
            isUpNext: item.isUpNext,
            songTitle: item.songTitle,
            songArtist: item.songArtist,
          }));

          const uniqueQueueData = Array.from(
            new Map(
              parsedData.map(item => [`${item.songId}-${item.requestorUserName}`, item])
            ).values()
          ).filter(item => item.sungAt == null && !item.wasSkipped);
          const userQueue = uniqueQueueData.filter(item => item.requestorUserName === userName && item.sungAt == null && !item.wasSkipped);
          console.log(`[FETCH_QUEUE] Fetched queue for event ${currentEvent.eventId} - total items: ${uniqueQueueData.length}, user items: ${userQueue.length}, userName: ${userName}`, userQueue);

          setMyQueues(prev => ({
            ...prev,
            [currentEvent.eventId]: userQueue.sort((a, b) => (a.position || 0) - (b.position || 0)),
          }));

          if (checkedIn && currentEvent.status.toLowerCase() === "live") {
            console.log(`[FETCH_QUEUE] Setting globalQueue for live event ${currentEvent.eventId}`);
            setGlobalQueue(uniqueQueueData.sort((a, b) => (a.position || 0) - (b.position || 0)));
          } else {
            console.log(`[FETCH_QUEUE] Clearing globalQueue: checkedIn=${checkedIn}, eventStatus=${currentEvent.status}`);
            setGlobalQueue([]);
            setQueueMessage(checkedIn ? "Event is not live." : "You are not checked in to the event.");
          }

          const songDetails: { [songId: number]: Song } = {};
          for (const item of uniqueQueueData) {
            if (!songDetailsMap[item.songId]) {
              try {
                console.log(`[FETCH_SONG] Fetching details for song ${item.songId}`);
                const songResponse = await fetch(`${API_ROUTES.SONG_BY_ID}/${item.songId}`, {
                  headers: { Authorization: `Bearer ${token}` },
                });
                if (!songResponse.ok) {
                  const errorText = await songResponse.text();
                  console.error(`[FETCH_SONG] Fetch song details failed for song ${item.songId}: ${songResponse.status} - ${errorText}`);
                  if (songResponse.status === 401) {
                    setFetchError("Session expired. Please log in again.");
                    localStorage.removeItem("token");
                    localStorage.removeItem("userName");
                    navigate("/login");
                    return;
                  }
                  throw new Error(`Fetch song details failed for song ${item.songId}: ${songResponse.status}`);
                }
                const songData = await songResponse.json();
                songDetails[item.songId] = {
                  ...songData,
                  title: songData.title || item.songTitle || `Song ${item.songId}`,
                  artist: songData.artist || item.songArtist || 'Unknown',
                };
              } catch (err) {
                console.error(`[FETCH_SONG] Error fetching song details for song ${item.songId}:`, err);
                songDetails[item.songId] = {
                  id: item.songId,
                  title: item.songTitle || `Song ${item.songId}`,
                  artist: item.songArtist || 'Unknown',
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
                  lastFmPlaycount: undefined,
                };
              }
            }
          }
          setSongDetailsMap(prev => ({ ...prev, ...songDetails }));
          setFetchError(null);
          setServerAvailable(true);
        } catch (err: unknown) {
          console.error(`[FETCH_QUEUE] Fetch queue error for event ${currentEvent?.eventId}:`, err);
          const errorMessage = err instanceof Error ? err.message : "Unknown error";
          if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
            setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
            setQueueMessage("Unable to load queue: Server is unreachable.");
            setServerAvailable(false);
          } else if (retryCount < maxQueueRetries) {
            retryCount++;
            console.log(`[FETCH_QUEUE] Retry ${retryCount}/${maxQueueRetries} in ${5000 * retryCount}ms`);
            setFetchError(`Failed to load queue (attempt ${retryCount}/${maxQueueRetries}). Retrying...`);
            setTimeout(attemptFetchQueue, 5000 * retryCount);
          } else {
            setGlobalQueue([]);
            setSongDetailsMap({});
            setFetchError("Failed to load the event queue. Please check your connection or contact support.");
            setQueueMessage("No songs in the queue or unable to load queue.");
            setServerAvailable(false);
          }
        }
      };

      await attemptFetchQueue();
    }, 5000),
    [currentEvent, checkedIn, isCurrentEventLive, navigate, songDetailsMap, validateToken]
  );

  // Force fetch queue on mount
  useEffect(() => {
    console.log("[DASHBOARD_MOUNT] Current event:", currentEvent ? { eventId: currentEvent.eventId, status: currentEvent.status } : null);
    console.log("[DASHBOARD_MOUNT] Context state:", { checkedIn, isCurrentEventLive });
    console.log("[DASHBOARD_MOUNT] Forcing queue fetch on mount");
    fetchQueue();
  }, [fetchQueue, currentEvent, checkedIn, isCurrentEventLive]);

  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { signalRError, setSignalRError, serverAvailable: signalRServerAvailable } = useSignalR({
    currentEvent,
    isCurrentEventLive,
    checkedIn,
    navigate,
    setGlobalQueue,
    setMyQueues,
    setSongDetailsMap,
    setHasAttemptedCheckIn,
    setCheckedIn,
    fetchQueue,
  }); // Suppress unused warning for setSignalRError; may be used in future logic

  // Update serverAvailable based on SignalR
  useEffect(() => {
    setServerAvailable(signalRServerAvailable);
  }, [signalRServerAvailable]);

  // Log token on mount
  useEffect(() => {
    console.log("[DASHBOARD_MOUNT] Logging token on mount");
    validateToken().then(token => {
      if (token) {
        console.log("[DASHBOARD_MOUNT] Full token:", token);
        try {
          const payload = JSON.parse(atob(token.split('.')[1]));
          console.log("[DASHBOARD_MOUNT] Decoded token payload:", payload);
        } catch (err) {
          console.error("[DASHBOARD_MOUNT] Failed to decode token payload:", err, { token });
        }
      } else {
        console.error("[DASHBOARD_MOUNT] No token found in localStorage");
      }
    });
  }, [validateToken]);

  // Check user roles
  useEffect(() => {
    const roles = JSON.parse(localStorage.getItem("roles") || "[]") as string[];
    console.log("[DASHBOARD_ROLES] Fetched roles:", roles);
    setIsSingerOnly(roles.length === 1 && roles.includes("Singer"));
  }, []);

  // Perform check-in when event becomes live and user is not checked in
  useEffect(() => {
    console.log("[CHECK_IN] Evaluating check-in conditions:", { currentEvent: currentEvent ? { eventId: currentEvent.eventId, status: currentEvent.status } : null, isCurrentEventLive, checkedIn, hasAttemptedCheckIn });
    if (!currentEvent || !isCurrentEventLive || checkedIn || hasAttemptedCheckIn) {
      console.log("[CHECK_IN] Skipping check-in: no current event, not live, already checked in, or attempted");
      return;
    }

    const checkIn = async () => {
      const isServerHealthy = await checkServerHealth();
      if (!isServerHealthy) {
        console.error("[CHECK_IN] Server health check failed, aborting check-in");
        return;
      }

      const token = await validateToken();
      if (!token) return;

      const userName = localStorage.getItem("userName");
      if (!userName) {
        console.error("[CHECK_IN] No userName found");
        setFetchError("User not found. Please log in again.");
        navigate("/login");
        return;
      }

      try {
        console.log(`[CHECK_IN] Checking attendance status for event: ${currentEvent.eventId}, token: ${token.slice(0, 10)}...`);
        const statusResponse = await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/status`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const statusText = await statusResponse.text();
        console.log("[CHECK_IN] Attendance Status Response:", { status: statusResponse.status, body: statusText });
        if (!statusResponse.ok) {
          if (statusResponse.status === 401) {
            console.error("[CHECK_IN] Session expired during status check");
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          throw new Error(`Failed to fetch attendance status: ${statusResponse.status} - ${statusText}`);
        }
        const statusData = JSON.parse(statusText);
        console.log("[CHECK_IN] Parsed attendance status:", statusData);
        if (statusData.isCheckedIn) {
          console.log(`[CHECK_IN] User already checked in for event ${currentEvent.eventId}`);
          setCheckedIn(true);
          setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
          setHasAttemptedCheckIn(true);
          await fetchQueue();
          return;
        }

        console.log(`[CHECK_IN] Checking into event: ${currentEvent.eventId}, payload:`, JSON.stringify({ RequestorId: userName }));
        const response = await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/check-in`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({ RequestorId: userName }),
        });

        const responseText = await response.text();
        console.log("[CHECK_IN] Check-in Response:", { status: response.status, body: responseText });
        if (!response.ok) {
          console.error(`[CHECK_IN] Check-in failed for event ${currentEvent.eventId}: ${response.status} - ${responseText}`);
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          if (response.status === 400 && responseText.includes("Requestor is already checked in")) {
            console.log(`[CHECK_IN] User already checked in for event ${currentEvent.eventId}`);
            setCheckedIn(true);
            setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
            setHasAttemptedCheckIn(true);
            await fetchQueue();
            return;
          }
          throw new Error(`Check-in failed: ${responseText || response.statusText}`);
        }
        console.log(`[CHECK_IN] Checked in successfully for event ${currentEvent.eventId}`);
        setCheckedIn(true);
        setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
        setHasAttemptedCheckIn(true);
        await fetchQueue();
      } catch (err) {
        console.error("[CHECK_IN] Check-in error:", err);
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
          setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
          setServerAvailable(false);
        } else {
          setFetchError("Failed to check in to the event. Please try again.");
        }
        setHasAttemptedCheckIn(true);
      }
    };

    checkIn();
  }, [currentEvent, isCurrentEventLive, checkedIn, hasAttemptedCheckIn, navigate, fetchQueue, setCheckedIn, setIsCurrentEventLive, validateToken]);

  const fetchSongs = async () => {
    if (!searchQuery.trim()) {
      console.log("[FETCH_SONGS] Search query is empty, resetting songs");
      setSongs([]);
      setShowSearchModal(false);
      setSearchError(null);
      return;
    }
    if (!serverAvailable) {
      console.error("[FETCH_SONGS] Server is not available, aborting fetch");
      setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    setIsSearching(true);
    setSearchError(null);
    console.log(`[FETCH_SONGS] Fetching songs with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SONGS_SEARCH}?query=${encodeURIComponent(searchQuery)}&page=1&pageSize=50`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[FETCH_SONGS] Fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Fetch failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log("[FETCH_SONGS] Fetch response:", data);
      const fetchedSongs = (data.songs as Song[]) || [];
      console.log("[FETCH_SONGS] Fetched songs:", fetchedSongs);
      const activeSongs = fetchedSongs.filter(song => song.status && song.status.toLowerCase() === "active");
      console.log("[FETCH_SONGS] Filtered active songs:", activeSongs);
      setSongs(activeSongs);

      if (activeSongs.length === 0) {
        setSearchError("There are no Karaoke songs available that match your search terms. Would you like to request a Karaoke song be added?");
        setShowSearchModal(true);
      } else {
        setShowSearchModal(true);
      }
    } catch (err) {
      console.error("[FETCH_SONGS] Search error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setSearchError("An error occurred while searching. Please try again.");
      }
      setSongs([]);
      setShowSearchModal(true);
    } finally {
      setIsSearching(false);
    }
  };

  const fetchSpotifySongs = async () => {
    if (!serverAvailable) {
      console.error("[FETCH_SPOTIFY] Server is not available, aborting fetch");
      setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    console.log(`[FETCH_SPOTIFY] Fetching songs from Spotify with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[FETCH_SPOTIFY] Spotify fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Spotify search failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log("[FETCH_SPOTIFY] Spotify fetch response:", data);
      const fetchedSpotifySongs = (data.songs as SpotifySong[]) || [];
      console.log("[FETCH_SPOTIFY] Fetched Spotify songs:", fetchedSpotifySongs);
      setSpotifySongs(fetchedSpotifySongs);
      setShowSpotifyModal(true);
      setShowSearchModal(false);
    } catch (err) {
      console.error("[FETCH_SPOTIFY] Spotify search error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setSearchError("An error occurred while searching Spotify. Please try again.");
      }
      setShowSearchModal(true);
    }
  };

  const handleSpotifySongSelect = (song: SpotifySong) => {
    console.log("[SPOTIFY_SELECT] Selected song:", song);
    setSelectedSpotifySong(song);
    setShowSpotifyDetailsModal(true);
  };

  const submitSongRequest = async (song: SpotifySong) => {
    console.log("[SUBMIT_SONG] Submitting song request:", song);
    if (!serverAvailable) {
      console.error("[SUBMIT_SONG] Server is not available, aborting request");
      setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("[SUBMIT_SONG] No userName found in localStorage");
      setSearchError("User ID missing. Please log in again.");
      navigate("/login");
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
      requestedBy: userName,
    };

    console.log("[SUBMIT_SONG] Sending song request payload:", requestData);

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
      console.log(`[SUBMIT_SONG] Song request response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`[SUBMIT_SONG] Failed to submit song request: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Song request failed: ${response.status} - ${responseText}`);
      }

      let result;
      if (responseText) {
        try {
          result = JSON.parse(responseText);
        } catch (error) {
          console.error("[SUBMIT_SONG] Failed to parse response as JSON:", responseText);
          throw new Error("Invalid response format from server");
        }
      }

      console.log("[SUBMIT_SONG] Parsed response:", result);
      console.log("[SUBMIT_SONG] Setting state: closing Spotify modal, opening confirmation");
      setRequestedSong(song);
      setShowSpotifyDetailsModal(false);
      setShowRequestConfirmationModal(true);
    } catch (err) {
      console.error("[SUBMIT_SONG] Song request error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setSearchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setSearchError("Failed to submit song request. Please try again.");
      }
    } finally {
      setIsSearching(false);
    }
  };

  const resetSearch = () => {
    console.log("[SEARCH] resetSearch called");
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

  const toggleFavorite = async (song: Song): Promise<void> => {
    console.log("[FAVORITE] toggleFavorite called with song:", song);
    if (!serverAvailable) {
      console.error("[FAVORITE] Server is not available, aborting request");
      setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    const isFavorite = favorites.some(fav => fav.id === song.id);
    const method = isFavorite ? 'DELETE' : 'POST';
    const url = isFavorite ? `${API_ROUTES.FAVORITES}/${song.id}` : API_ROUTES.FAVORITES;

    console.log(`[FAVORITE] Toggling favorite for song ${song.id}, isFavorite: ${isFavorite}, method: ${method}, url: ${url}`);

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
      console.log(`[FAVORITE] Toggle favorite response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`[FAVORITE] Failed to ${isFavorite ? 'remove' : 'add'} favorite: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`${isFavorite ? 'Remove' : 'Add'} favorite failed: ${response.status}`);
      }

      let result;
      try {
        result = JSON.parse(responseText);
      } catch (error) {
        console.error("[FAVORITE] Failed to parse response as JSON:", responseText);
        throw new Error("Invalid response format from server");
      }

      console.log(`[FAVORITE] Parsed toggle favorite response:`, result);

      if (result.success) {
        const updatedFavorites = isFavorite
          ? favorites.filter(fav => fav.id !== song.id)
          : [...favorites, { ...song }];
        console.log(`[FAVORITE] Updated favorites after ${isFavorite ? 'removal' : 'addition'}:`, updatedFavorites);
        setFavorites(updatedFavorites);
      } else {
        console.error("[FAVORITE] Toggle favorite failed: Success flag not set in response");
      }
    } catch (err) {
      console.error(`[FAVORITE] ${isFavorite ? 'Remove' : 'Add'} favorite error:`, err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setFetchError(`Failed to ${isFavorite ? 'remove' : 'add'} favorite. Please try again.`);
      }
    }
  };

  const addToEventQueue = async (song: Song, eventId: number): Promise<void> => {
    console.log("[QUEUE] addToEventQueue called with song:", song, "eventId:", eventId);
    if (!serverAvailable) {
      console.error("[QUEUE] Server is not available, cannot add to queue");
      setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    const userName = localStorage.getItem("userName");
    console.log("[QUEUE] addToEventQueue - token:", token.slice(0, 10), "...", "requestorUserName:", userName);

    if (!userName) {
      console.error("[QUEUE] Invalid or missing requestorUserName in addToEventQueue");
      setFetchError("User not found. Please log in again to add songs to the queue.");
      navigate("/login");
      return;
    }

    const queueForEvent = myQueues[eventId] || [];
    const isInQueue = queueForEvent.some(q => q.songId === song.id);
    if (isInQueue) {
      console.log(`[QUEUE] Song ${song.id} is already in the queue for event ${eventId}`);
      return;
    }

    try {
      const requestBody = JSON.stringify({
        songId: song.id,
        requestorUserName: userName,
      });
      console.log("[QUEUE] addToEventQueue - Sending request to:", `${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, "with body:", requestBody);

      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: requestBody,
      });

      const responseText = await response.text();
      console.log(`[QUEUE] Add to queue response for event ${eventId}: status=${response.status}, body=${responseText}`);

      if (!response.ok) {
        console.error(`[QUEUE] Failed to add song to queue for event ${eventId}: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Failed to add song: ${responseText || response.statusText}`);
      }

      await fetchQueue(); // Force fetch to update queue immediately
    } catch (err) {
      console.error("[QUEUE] Add to queue error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setFetchError("Failed to add song to queue. Please try again.");
      }
    }
  };

  const handleDeleteSong = async (eventId: number, queueId: number): Promise<void> => {
    console.log("[QUEUE] handleDeleteSong called with eventId:", eventId, "queueId:", queueId);
    if (!serverAvailable) {
      console.error("[QUEUE] Server is not available, cannot delete song");
      setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = await validateToken();
    if (!token) return;

    try {
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue/${queueId}/skip`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[QUEUE] Skip song failed: ${response.status} - ${errorText}`);
        if (response.status === 401) {
          setFetchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Skip song failed: ${response.status} - ${errorText}`);
      }

      await fetchQueue(); // Force fetch to update queue immediately
    } catch (err) {
      console.error("[QUEUE] Skip song error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setFetchError("Failed to remove song from queue. Please try again.");
      }
      setMyQueues(prev => ({
        ...prev,
        [eventId]: (prev[eventId] || []).filter(q => q.queueId !== queueId),
      }));
      setGlobalQueue(prev => prev.filter(q => q.queueId !== queueId));
    }
  };

  const handleQueueItemClick = (song: Song, queueId: number, eventId: number) => {
    console.log("[QUEUE] handleQueueItemClick called with song:", song, "queueId:", queueId, "eventId:", eventId);
    setSelectedSong(song);
    setSelectedQueueId(queueId);
  };

  const handleGlobalQueueItemClick = (song: Song) => {
    console.log("[QUEUE] handleGlobalQueueItemClick called with song:", song);
    setSelectedSong(song);
  };

  const handleDragEnd = async (event: DragEndEvent) => {
    console.log("[DRAG] handleDragEnd called with event:", event);
    const { active, over } = event;

    if (!active || !over || active.id === over.id) {
      console.log("[DRAG] No action needed - same position or invalid drag");
      return;
    }

    if (!currentEvent) {
      console.error("[DRAG] No current event selected");
      setReorderError("No event selected. Please select an event and try again.");
      setShowReorderErrorModal(true);
      toast.error("No event selected. Please select an event and try again.");
      return;
    }

    if (!serverAvailable) {
      console.error("[DRAG] Server is not available, cannot reorder");
      setReorderError("Unable to connect to the server. Please check if the server is running and try again.");
      setShowReorderErrorModal(true);
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }

    const currentQueue = myQueues[currentEvent.eventId] || [];
    console.log("[DRAG] Current queue before reorder:", currentQueue);

    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("[DRAG] No userName found");
      setReorderError("User not found. Please log in again.");
      setShowReorderErrorModal(true);
      toast.error("User not found. Please log in again.");
      navigate("/login");
      return;
    }

    const oldIndex = currentQueue.findIndex(item => item.queueId === active.id);
    const newIndex = currentQueue.findIndex(item => item.queueId === over.id);
    console.log(`[DRAG] Moving item from index ${oldIndex} to ${newIndex}`);

    // Validate that oldSlot and newSlot belong to the user
    const activeItem = currentQueue[oldIndex];
    const overItem = currentQueue[newIndex];
    if (!activeItem || !overItem || activeItem.requestorUserName !== userName || overItem.requestorUserName !== userName) {
      console.error("[DRAG] Invalid reordering attempt: slots do not belong to user", { activeItem, overItem, userName });
      setReorderError("Cannot reorder: Invalid slots for your queue.");
      setShowReorderErrorModal(true);
      toast.error("Cannot reorder: Invalid slots for your queue.");
      return;
    }

    const oldSlot = activeItem.position;
    const newSlot = overItem.position;
    const reorder = [{ queueId: activeItem.queueId, oldSlot, newSlot }];
    console.log("[DRAG] Personal reorder payload:", reorder);

    const token = await validateToken();
    if (!token) return;

    try {
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue/personal/reorder`, {
        method: 'PUT',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ reorder }),
      });

      const responseText = await response.text();
      console.log(`[DRAG] Reorder response: status=${response.status}, body=${responseText}`);

      if (!response.ok) {
        console.error(`[DRAG] Reorder failed: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setReorderError("Session expired. Please log in again.");
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        setReorderError(`Failed to reorder: ${responseText || 'Invalid slot'}`);
        setShowReorderErrorModal(true);
        toast.error(`Failed to reorder: ${responseText || 'Invalid slot'}`);
        return;
      }

      toast.success('Songs reordered within your slots');
      setReorderError(null);
      setShowReorderErrorModal(false);
    } catch (err) {
      console.error("[DRAG] Reorder error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        setReorderError("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        setReorderError("Failed to reorder queue. Please try again or contact support.");
      }
      setShowReorderErrorModal(true);
      toast.error("Failed to reorder queue. Please try again or contact support.");
    }
  };

  // Fetch favorites on mount with retry logic
  const maxRetries = 3;
  useEffect(() => {
    const attemptFetchFavorites = async (retryCount = 0) => {
      if (!serverAvailable) {
        console.error("[FAVORITES] Server is not available, aborting fetch");
        setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
        return;
      }
      const token = await validateToken();
      if (!token) return;

      console.log(`[FAVORITES] Fetching favorites from: ${API_ROUTES.FAVORITES}, attempt ${retryCount + 1}/${maxRetries}`);
      try {
        const response = await fetch(`${API_ROUTES.FAVORITES}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) {
          const errorText = await response.text();
          console.error(`[FAVORITES] Fetch favorites failed with status: ${response.status}, response: ${errorText}`);
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch favorites failed: ${response.status} - ${errorText}`);
        }
        const data: Song[] = await response.json();
        console.log("[FAVORITES] Fetched favorites:", data);
        setFavorites(data || []);
        setFetchError(null);
        setServerAvailable(true);
      } catch (err: unknown) {
        console.error("[FAVORITES] Fetch favorites error:", err);
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
          setFetchError("Unable to connect to the server. Please check if the server is running and try again.");
          setServerAvailable(false);
        } else if (retryCount < maxRetries) {
          setFetchError(`Failed to load favorites (attempt ${retryCount + 1}/${maxRetries}). Retrying...`);
          setTimeout(() => attemptFetchFavorites(retryCount + 1), 5000 * (retryCount + 1));
        } else {
          setFavorites([]);
          setFetchError("Failed to load favorites. Please check your connection or contact support.");
          setServerAvailable(false);
        }
      }
    };

    attemptFetchFavorites();
  }, [navigate, validateToken, serverAvailable]);

  return (
    <div className="dashboard">
      <Toaster />
      <div className="dashboard-content">
        {fetchError && <p className="error-text">{fetchError}</p>}
        {signalRError && <p className="error-text">{signalRError}</p>}
        {queueMessage && <p className="info-text">{queueMessage}</p>}
        <SearchBar
          searchQuery={searchQuery}
          setSearchQuery={setSearchQuery}
          fetchSongs={fetchSongs}
          resetSearch={resetSearch}
          navigate={navigate}
        />
        <div className="main-content">
          <QueuePanel
            currentEvent={currentEvent}
            checkedIn={checkedIn}
            isCurrentEventLive={isCurrentEventLive}
            myQueues={myQueues}
            songDetailsMap={songDetailsMap}
            reorderError={reorderError}
            showReorderErrorModal={showReorderErrorModal}
            handleQueueItemClick={handleQueueItemClick}
            handleDragEnd={handleDragEnd}
            enableDragAndDrop={serverAvailable && checkedIn}
          />
          <GlobalQueuePanel
            currentEvent={currentEvent}
            checkedIn={checkedIn}
            isCurrentEventLive={isCurrentEventLive}
            globalQueue={globalQueue}
            myQueues={myQueues}
            songDetailsMap={songDetailsMap}
            handleGlobalQueueItemClick={handleGlobalQueueItemClick}
            enableDragAndDrop={false}
          />
          <FavoritesSection
            favorites={favorites}
            setSelectedSong={setSelectedSong}
          />
        </div>
        <Modals
          isSearching={isSearching}
          searchError={searchError}
          songs={songs}
          spotifySongs={spotifySongs}
          selectedSpotifySong={selectedSpotifySong}
          requestedSong={requestedSong}
          selectedSong={selectedSong}
          showSearchModal={showSearchModal}
          showSpotifyModal={showSpotifyModal}
          showSpotifyDetailsModal={showSpotifyDetailsModal}
          showRequestConfirmationModal={showRequestConfirmationModal}
          showReorderErrorModal={showReorderErrorModal}
          reorderError={reorderError}
          fetchSpotifySongs={fetchSpotifySongs}
          handleSpotifySongSelect={handleSpotifySongSelect}
          submitSongRequest={submitSongRequest}
          resetSearch={resetSearch}
          setSelectedSong={setSelectedSong}
          setShowReorderErrorModal={setShowReorderErrorModal}
          setShowSpotifyDetailsModal={setShowSpotifyDetailsModal}
          setSearchError={setSearchError}
          setSelectedQueueId={setSelectedQueueId}
          favorites={favorites}
          myQueues={myQueues}
          isSingerOnly={isSingerOnly}
          toggleFavorite={isSingerOnly ? undefined : toggleFavorite}
          addToEventQueue={isSingerOnly ? undefined : addToEventQueue}
          handleDeleteSong={isSingerOnly ? undefined : (currentEvent && selectedQueueId ? handleDeleteSong : undefined)}
          currentEvent={currentEvent}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
          selectedQueueId={selectedQueueId}
        />
      </div>
    </div>
  );
};

export default Dashboard;