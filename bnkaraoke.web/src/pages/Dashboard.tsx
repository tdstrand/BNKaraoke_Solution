// src/pages/Dashboard.tsx
import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { DragEndEvent } from '@dnd-kit/core';
import toast from 'react-hot-toast';
import Fuse from 'fuse.js';
import './Dashboard.css';
import { API_ROUTES } from '../config/apiConfig';
import { Song, SpotifySong, EventQueueItem } from '../types';
import { useEventContext } from '../context/EventContext';
import useSignalR from '../hooks/useSignalR';
import SearchBar from '../components/SearchBar';
import QueuePanel from '../components/QueuePanel';
import GlobalQueuePanel from '../components/GlobalQueuePanel';
import FavoritesSection from '../components/FavoritesSection';
import Modals from '../components/Modals';

const Dashboard: React.FC = () => {
  const navigate = useNavigate();
  const { checkedIn, isCurrentEventLive, currentEvent, setIsOnBreak, logout } = useEventContext();
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
  const [serverAvailable, setServerAvailable] = useState<boolean>(true);

  useEffect(() => {
    console.log("[DASHBOARD_INIT] Initial state:", { currentEvent: currentEvent ? { eventId: currentEvent.eventId, status: currentEvent.status } : null, checkedIn, isCurrentEventLive });
  }, [currentEvent, checkedIn, isCurrentEventLive]);

  const validateToken = useCallback((): string | null => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[VALIDATE_TOKEN] No token or userName found", { token: !!token, userName: !!userName });
      logout("Authentication token or username missing. Please log in again.");
      return null;
    }
    try {
      console.log("[VALIDATE_TOKEN] Token length:", token.length);
      if (token.split('.').length !== 3) {
        console.error("[VALIDATE_TOKEN] Malformed token: does not contain three parts", { parts: token.split('.') });
        logout("Invalid token format. Please log in again.");
        return null;
      }
      const payload = JSON.parse(atob(token.split('.')[1]));
      console.log("[VALIDATE_TOKEN] Decoded payload:", payload);
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[VALIDATE_TOKEN] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        logout("Session expired. Please log in again.");
        return null;
      }
      console.log("[VALIDATE_TOKEN] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[VALIDATE_TOKEN] Error:", err, { token });
      logout("Invalid token. Please log in again.");
      return null;
    }
  }, [logout]);

  const { signalRError, serverAvailable: signalRServerAvailable, queuesLoading } = useSignalR({
    currentEvent,
    isCurrentEventLive,
    checkedIn,
    navigate,
    setGlobalQueue,
    setMyQueues,
    setSongDetailsMap,
    setIsOnBreak,
  });

  useEffect(() => {
    setServerAvailable(signalRServerAvailable);
  }, [signalRServerAvailable]);

  useEffect(() => {
    const token = validateToken();
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
  }, [validateToken]);

  useEffect(() => {
    const roles = JSON.parse(localStorage.getItem("roles") || "[]") as string[];
    console.log("[DASHBOARD_ROLES] Fetched roles:", roles);
    setIsSingerOnly(roles.length === 1 && roles.includes("Singer"));
  }, []);

  const fetchSongs = useCallback(async () => {
    if (!searchQuery.trim()) {
      console.log("[FETCH_SONGS] Search query is empty, resetting songs");
      setSongs([]);
      setShowSearchModal(false);
      setSearchError(null);
      return;
    }
    if (!serverAvailable) {
      console.error("[FETCH_SONGS] Server is not available, aborting fetch");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
    if (!token) return;
    setIsSearching(true);
    setSearchError(null);
    console.log(`[FETCH_SONGS] Fetching songs with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SONGS_SEARCH}?query=${encodeURIComponent(searchQuery)}&page=1&pageSize=100`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[FETCH_SONGS] Fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
          navigate("/login");
          return;
        }
        throw new Error(`Fetch failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log("[FETCH_SONGS] Fetch response:", data);
      const fetchedSongs = (data.songs as Song[]) || [];
      console.log("[FETCH_SONGS] Fetched songs:", fetchedSongs);
      console.log("[FETCH_SONGS] Fetched songs statuses:", fetchedSongs.map(song => song.status));
      // Apply Fuse.js for fuzzy search
      const fuse = new Fuse(fetchedSongs, {
        keys: ['title', 'artist'],
        threshold: 0.3,
      });
      const fuseResult = fuse.search(searchQuery);
      const fuzzySongs = fuseResult.map(result => result.item);
      setSongs(fuzzySongs);
      if (fuzzySongs.length === 0) {
        setSearchError("There are no songs in the database that match your search terms. Would you like to request a song?");
        setShowSearchModal(true);
      } else {
        setShowSearchModal(true);
      }
    } catch (err) {
      console.error("[FETCH_SONGS] Search error:", err);
      const errorMessage = err instanceof Error ? err.message : "Unknown error";
      if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
        toast.error("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        toast.error("An error occurred while searching. Please try again.");
      }
      setSongs([]);
      setShowSearchModal(true);
    } finally {
      setIsSearching(false);
    }
    }, [serverAvailable, validateToken, navigate, searchQuery]);

  const fetchSpotifySongs = useCallback(async () => {
    if (!serverAvailable) {
      console.error("[FETCH_SPOTIFY] Server is not available, aborting fetch");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
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
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
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
        toast.error("Unable to connect to the server. Please check if the server is running and try again.");
        setServerAvailable(false);
      } else {
        toast.error("An error occurred while searching Spotify. Please try again.");
      }
      setShowSpotifyModal(true);
    }
    }, [serverAvailable, validateToken, navigate, searchQuery]);

  const handleSpotifySongSelect = useCallback((song: SpotifySong) => {
    console.log("[SPOTIFY_SELECT] Selected song:", song);
    setSelectedSpotifySong(song);
    setShowSpotifyDetailsModal(true);
  }, []);

  const submitSongRequest = useCallback(async (song: SpotifySong) => {
    console.log("[SUBMIT_SONG] Submitting song request:", song);
    if (!serverAvailable) {
      console.error("[SUBMIT_SONG] Server is not available, aborting request");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
    if (!token) return;
    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("[SUBMIT_SONG] No userName found in localStorage");
      toast.error("User ID missing. Please log in again.");
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
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });
      const responseText = await response.text();
      console.log(`[SUBMIT_SONG] Song request response status: ${response.status}, body: ${responseText}`);
      if (!response.ok) {
        console.error(`[SUBMIT_SONG] Failed to submit song request: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
          navigate("/login");
          return;
        }
        if (response.status === 400 && responseText.includes("already exists")) {
          const songResponse = await fetch(`${API_ROUTES.SONG_BY_ID}?spotifyId=${encodeURIComponent(song.id)}`, {
            headers: { Authorization: `Bearer ${token}` },
          });
          if (songResponse.ok) {
            const songData = await songResponse.json();
            const status = songData.status === 'active' ? 'Available' : songData.status === 'pending' ? 'Pending' : 'Unavailable';
            setSearchError(`Song "${requestData.title}" by ${requestData.artist} already exists in the database with status: ${status}.`);
            setShowRequestConfirmationModal(false);
            setShowSpotifyModal(false);
            setShowSpotifyDetailsModal(false);
          } else {
            setSearchError(`Song "${requestData.title}" by ${requestData.artist} already exists in the database.`);
          }
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
      toast.success("Song request submitted successfully!");
    } catch (err) {
      console.error("[SUBMIT_SONG] Song request error:", err);
      toast.error("Failed to submit song request. Please try again.");
      } finally {
        setIsSearching(false);
      }
    }, [serverAvailable, validateToken, navigate]);

  const resetSearch = useCallback(() => {
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
  }, []);

  const toggleFavorite = useCallback(async (song: Song): Promise<void> => {
    if (song.status?.toLowerCase() !== 'active') {
      toast.error('Only Available songs can be added to favorites.');
      return;
    }
    console.log("[FAVORITE] toggleFavorite called with song:", song);
    if (!serverAvailable) {
      console.error("[FAVORITE] Server is not available, aborting request");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
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
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
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
        console.log(`[FAVORITE] Updated favorites after ${isFavorite ? 'remove' : 'add'}:`, updatedFavorites);
        setFavorites(updatedFavorites);
        toast.success(`Song ${isFavorite ? 'removed from' : 'added to'} favorites!`);
      } else {
        console.error("[FAVORITE] Toggle favorite failed: Success flag not set in response");
        toast.error(`Failed to ${isFavorite ? 'remove' : 'add'} favorite. Please try again.`);
      }
    } catch (err) {
      console.error(`[FAVORITE] ${isFavorite ? 'Remove' : 'Add'} favorite error:`, err);
      toast.error("Failed to update favorites. Please try again.");
    }
  }, [favorites, serverAvailable, validateToken, navigate]);

  const addToEventQueue = useCallback(async (song: Song, eventId: number): Promise<void> => {
    const status = song.status?.toLowerCase();
    if (!status || !['active', 'available'].includes(status)) {
      toast.error('Only Available songs can be added to the queue.');
      return;
    }
    console.log("[QUEUE] addToEventQueue called with song:", song, "eventId:", eventId);
    if (!serverAvailable) {
      console.error("[QUEUE] Server is not available, cannot add to queue");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
    if (!token) return;
    const userName = localStorage.getItem("userName");
    console.log("[QUEUE] addToEventQueue - token:", token.slice(0, 10), "...", "requestorUserName:", userName);
    if (!userName) {
      console.error("[QUEUE] Invalid or missing requestorUserName in addToEventQueue");
      toast.error("User not found. Please log in again to add songs to the queue.");
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
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          songId: song.id,
          requestorUserName: userName,
        }),
      });
      const responseText = await response.text();
      console.log(`[QUEUE] Add to queue response for event ${eventId}: status=${response.status}, body=${responseText}`);
      if (!response.ok) {
        console.error(`[QUEUE] Failed to add song to queue for event ${eventId}: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
          navigate("/login");
          return;
        }
        throw new Error(`Add to queue failed: ${response.status} - ${responseText}`);
      }
      toast.success("Song added to queue successfully!");
    } catch (err) {
      console.error("[QUEUE] Add to queue error:", err);
      toast.error("Failed to add song to queue. Please try again.");
    }
  }, [myQueues, serverAvailable, validateToken, navigate]);

  const handleDeleteSong = useCallback(async (eventId: number, queueId: number): Promise<void> => {
    console.log("[QUEUE] handleDeleteSong called with eventId:", eventId, "queueId:", queueId);
    if (!serverAvailable) {
      console.error("[QUEUE] Server is not available, cannot delete song");
      toast.error("Unable to connect to the server. Please check if the server is running and try again.");
      return;
    }
    const token = validateToken();
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
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
          navigate("/login");
          return;
        }
        throw new Error(`Skip song failed: ${response.status} - ${errorText}`);
      }
      toast.success("Song removed from queue successfully!");
    } catch (err) {
      console.error("[QUEUE] Skip song error:", err);
      toast.error("Failed to remove song from queue. Please try again.");
      setMyQueues(prev => ({
        ...prev,
        [eventId]: (prev[eventId] || []).filter(q => q.queueId !== queueId),
      }));
      setGlobalQueue(prev => prev.filter(q => q.queueId !== queueId));
    }
  }, [serverAvailable, validateToken, navigate, setMyQueues, setGlobalQueue]);

  const handleQueueItemClick = useCallback((song: Song, queueId: number, eventId: number) => {
    console.log("[QUEUE] handleQueueItemClick called with song:", song, "queueId:", queueId, "eventId:", eventId);
    setSelectedSong(song);
    setSelectedQueueId(queueId);
  }, [setSelectedSong, setSelectedQueueId]);

  const handleGlobalQueueItemClick = useCallback((song: Song) => {
    console.log("[QUEUE] handleGlobalQueueItemClick called with song:", song);
    setSelectedSong(song);
  }, [setSelectedSong]);

  const handleDragEnd = useCallback(async (event: DragEndEvent) => {
    console.log("[DRAG] Debug: handleDragEnd triggered with event:", event);
    console.log("[DRAG] Active and Over IDs:", { activeId: event.active.id, overId: event.over?.id });
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
    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("[DRAG] No userName found");
      setReorderError("User not found. Please log in again.");
      setShowReorderErrorModal(true);
      toast.error("User not found. Please log in again.");
      navigate("/login");
      return;
    }
    const currentQueue = myQueues[currentEvent.eventId] || [];
    console.log("[DRAG] Filtered myQueues:", currentQueue);
    const reorderableQueue = currentQueue.filter(item => !item.isCurrentlyPlaying);
    console.log("[DRAG] Reorderable queue:", reorderableQueue);
    const activeExists = reorderableQueue.some(item => item.queueId.toString() === active.id);
    const overExists = reorderableQueue.some(item => item.queueId.toString() === over.id);
    console.log("[DRAG] Queue ID validation:", { activeId: active.id, activeExists, overId: over.id, overExists });
    if (!activeExists || !overExists) {
      console.error("[DRAG] Invalid queue IDs: not found in reorderable queue", { activeId: active.id, overId: over.id, reorderableQueue });
      setReorderError("Cannot reorder: Invalid queue IDs. Resetting queue.");
      setShowReorderErrorModal(true);
      toast.error("Cannot reorder: Invalid queue IDs. Resetting queue.");
      setMyQueues(prev => ({ ...prev, [currentEvent.eventId]: [] }));
      return;
    }
    const oldIndex = reorderableQueue.findIndex(item => item.queueId.toString() === active.id);
    const newIndex = reorderableQueue.findIndex(item => item.queueId.toString() === over.id);
    const activeItem = reorderableQueue[oldIndex];
    const overItem = reorderableQueue[newIndex];
    console.log("[DRAG] Validation details:", {
      oldIndex,
      newIndex,
      activeItem: activeItem ? { queueId: activeItem.queueId, requestorUserName: activeItem.requestorUserName, position: activeItem.position } : null,
      overItem: overItem ? { queueId: overItem.queueId, requestorUserName: overItem.requestorUserName, position: overItem.position } : null,
      userName,
    });
    if (!activeItem || !overItem || activeItem.requestorUserName !== userName || overItem.requestorUserName !== userName) {
      console.error("[DRAG] Invalid reordering attempt: slots do not belong to user", {
        activeItem: activeItem ? { queueId: activeItem.queueId, requestorUserName: activeItem.requestorUserName, position: activeItem.position } : null,
        overItem: overItem ? { queueId: overItem.queueId, requestorUserName: overItem.requestorUserName, position: overItem.position } : null,
        userName,
      });
      setReorderError("Cannot reorder: Invalid slots for your queue.");
      setShowReorderErrorModal(true);
      toast.error("Cannot reorder: Invalid slots for your queue.");
      return;
    }
    if (activeItem.isCurrentlyPlaying || overItem.isCurrentlyPlaying) {
      console.error("[DRAG] Invalid reordering attempt: cannot reorder currently playing songs", {
        activeItem: activeItem ? { queueId: activeItem.queueId, isCurrentlyPlaying: activeItem.isCurrentlyPlaying } : null,
        overItem: overItem ? { queueId: overItem.queueId, isCurrentlyPlaying: overItem.isCurrentlyPlaying } : null,
      });
      setReorderError("Cannot reorder: Currently playing songs cannot be moved.");
      setShowReorderErrorModal(true);
      toast.error("Cannot reorder: Currently playing songs cannot be moved.");
      return;
    }
    const oldSlot = activeItem.position;
    const newSlot = overItem.position;
    console.log("[DRAG] Slot details:", { oldSlot, newSlot });
    if (oldSlot == null || newSlot == null) {
      console.error("[DRAG] Invalid position values", { oldSlot, newSlot });
      setReorderError("Cannot reorder: Invalid position values.");
      setShowReorderErrorModal(true);
      toast.error("Cannot reorder: Invalid position values.");
      return;
    }
    const reorder = reorderableQueue.map(item => ({
      queueId: item.queueId,
      oldSlot: item.position,
      newSlot: item.queueId === activeItem.queueId ? newSlot : item.queueId === overItem.queueId ? oldSlot : item.position,
    }));
    console.log("[DRAG] Full reorder payload:", reorder);
    const token = validateToken();
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
          toast.error("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          localStorage.removeItem("firstName");
          localStorage.removeItem("lastName");
          localStorage.removeItem("roles");
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
      toast.error("Failed to reorder queue. Please try again.");
    }
    }, [currentEvent, serverAvailable, navigate, myQueues, setMyQueues, validateToken]);

  const maxRetries = 1;
  useEffect(() => {
    const attemptFetchFavorites = async (retryCount = 0) => {
      const token = validateToken();
      if (!token) return;
      console.log(`[FAVORITES] Fetching favorites from: ${API_ROUTES.FAVORITES}, attempt ${retryCount + 1}/${maxRetries + 1}`);
      try {
        const response = await fetch(`${API_ROUTES.FAVORITES}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (!response.ok) {
          const errorText = await response.text();
          console.error(`[FAVORITES] Fetch favorites failed with status: ${response.status}, response: ${errorText}`);
          if (response.status === 401) {
            toast.error("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            localStorage.removeItem("firstName");
            localStorage.removeItem("lastName");
            localStorage.removeItem("roles");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch favorites failed: ${response.status} - ${errorText}`);
        }
        const data: Song[] = await response.json();
        console.log("[FAVORITES] Fetched favorites:", data);
        setFavorites(data || []);
        setFetchError(null);
      } catch (err) {
        console.error("[FAVORITES] Fetch favorites error:", err);
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        if (errorMessage.includes("ERR_CONNECTION_REFUSED") && retryCount < maxRetries) {
          console.log(`[FAVORITES] Retrying in ${2000 * (retryCount + 1)}ms...`);
          toast.error(`Failed to load favorites (attempt ${retryCount + 1}/${maxRetries + 1}). Retrying...`);
          setTimeout(() => attemptFetchFavorites(retryCount + 1), 2000 * (retryCount + 1));
        } else {
          setFavorites([]);
          setFetchError("Failed to load favorites. Please try again or contact support.");
          toast.error("Failed to load favorites. Please try again or contact support.");
        }
      }
    };
    attemptFetchFavorites();
  }, [navigate, validateToken]);

  const MemoizedQueuePanel = useMemo(() => (
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
      isLoading={queuesLoading}
    />
  ), [currentEvent, checkedIn, isCurrentEventLive, myQueues, songDetailsMap, reorderError, showReorderErrorModal, handleQueueItemClick, handleDragEnd, serverAvailable, queuesLoading]);

  const MemoizedGlobalQueuePanel = useMemo(() => (
    <GlobalQueuePanel
      currentEvent={currentEvent}
      checkedIn={checkedIn}
      isCurrentEventLive={isCurrentEventLive}
      globalQueue={globalQueue}
      myQueues={myQueues}
      songDetailsMap={songDetailsMap}
      handleGlobalQueueItemClick={handleGlobalQueueItemClick}
      enableDragAndDrop={false}
      isLoading={queuesLoading}
    />
  ), [currentEvent, checkedIn, isCurrentEventLive, globalQueue, myQueues, songDetailsMap, handleGlobalQueueItemClick, queuesLoading]);

  const [isMobile, setIsMobile] = useState(window.matchMedia("(max-width: 767px)").matches);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(max-width: 767px)");
    const handleResize = () => setIsMobile(mediaQuery.matches);
    handleResize();
    mediaQuery.addEventListener("change", handleResize);
    return () => mediaQuery.removeEventListener("change", handleResize);
  }, []);

  return (
    <div className={`dashboard${isMobile ? " mobile-dashboard" : ""}`}>
      <div className="dashboard-content">
        {fetchError && <p className="error-text">{fetchError}</p>}
        {signalRError && <p className="error-text">{signalRError}</p>}
        <SearchBar
          searchQuery={searchQuery}
          setSearchQuery={setSearchQuery}
          fetchSongs={fetchSongs}
          resetSearch={resetSearch}
          navigate={navigate}
          isSearching={isSearching}
        />
        <div className="main-content">
          {checkedIn && isCurrentEventLive && MemoizedQueuePanel}
          {checkedIn && isCurrentEventLive && MemoizedGlobalQueuePanel}
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
          setShowSpotifyModal={setShowSpotifyModal}
          setShowSpotifyDetailsModal={setShowSpotifyDetailsModal}
          setShowRequestConfirmationModal={setShowRequestConfirmationModal}
          setRequestedSong={setRequestedSong}
          setSearchError={setSearchError}
          setSelectedQueueId={setSelectedQueueId}
          favorites={favorites}
          myQueues={myQueues}
          toggleFavorite={toggleFavorite}
          addToEventQueue={addToEventQueue}
          handleDeleteSong={!isSingerOnly && currentEvent && selectedQueueId ? handleDeleteSong : undefined}
          currentEvent={currentEvent}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
          selectedQueueId={selectedQueueId}
          requestNewSong={fetchSpotifySongs}
        />
      </div>
    </div>
  );
};

export default Dashboard;