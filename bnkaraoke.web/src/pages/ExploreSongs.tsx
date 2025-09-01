// src/pages/ExploreSongs.tsx
import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_ROUTES } from '../config/apiConfig';
import SongDetailsModal from '../components/SongDetailsModal';
import Modals from '../components/Modals';
import './ExploreSongs.css';
import { Song, EventQueueItem, SpotifySong } from '../types';
import { useEventContext } from '../context/EventContext';
import toast from 'react-hot-toast';
import { SearchOutlined, CloseOutlined, LoadingOutlined } from '@ant-design/icons';

const ExploreSongs: React.FC = () => {
  const navigate = useNavigate();
  const { checkedIn, isCurrentEventLive, currentEvent, liveEvents } = useEventContext();
  const [queues, setQueues] = useState<{ [eventId: number]: EventQueueItem[] }>({});
  const [favorites, setFavorites] = useState<Song[]>([]);
  const [artistFilter, setArtistFilter] = useState<string>('All Artists');
  const [decadeFilter, setDecadeFilter] = useState<string>('All Decades');
  const [genreFilter, setGenreFilter] = useState<string>('All Genres');
  const [popularityFilter, setPopularityFilter] = useState<string>('All Popularities');
  const [requestedByFilter, setRequestedByFilter] = useState<string>('All Requests');
  const [statusFilter, setStatusFilter] = useState<string>(' Status : All');
  const [showFilterDropdown, setShowFilterDropdown] = useState<string | null>(null);
  const [browseSongs, setBrowseSongs] = useState<Song[]>([]);
  const [page, setPage] = useState<number>(1);
  const [pageSize, setPageSize] = useState<number>(window.matchMedia('(max-width: 767px)').matches ? 10 : 75);
  const [totalPages, setTotalPages] = useState<number>(1);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [selectedSong, setSelectedSong] = useState<Song | null>(null);
  const [artists, setArtists] = useState<string[]>(['All Artists']);
  const [genres, setGenres] = useState<string[]>(['All Genres']);
  const [artistError, setArtistError] = useState<string | null>(null);
  const [genreError, setGenreError] = useState<string | null>(null);
  const [queueError, setQueueError] = useState<string | null>(null);
  const [filterError, setFilterError] = useState<string | null>(null);
  const [spotifySongs, setSpotifySongs] = useState<SpotifySong[]>([]);
  const [selectedSpotifySong, setSelectedSpotifySong] = useState<SpotifySong | null>(null);
  const [showSpotifyModal, setShowSpotifyModal] = useState<boolean>(false);
  const [showSpotifyDetailsModal, setShowSpotifyDetailsModal] = useState<boolean>(false);
  const [showRequestConfirmationModal, setShowRequestConfirmationModal] = useState<boolean>(false);
  const [requestedSong, setRequestedSong] = useState<SpotifySong | null>(null);
  const [isSearching, setIsSearching] = useState<boolean>(false);
  const [serverAvailable, setServerAvailable] = useState<boolean>(true);
  const [spotifySearchQuery, setSpotifySearchQuery] = useState<string>('');
  const maxRetries = 3;

  const validateToken = useCallback(() => {
    const token = localStorage.getItem('token');
    const userName = localStorage.getItem('userName');
    if (!token || !userName) {
      console.error('[EXPLORE_SONGS] No token or userName found');
      setFilterError('Authentication token or username missing. Please log in again.');
      navigate('/login');
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error('[EXPLORE_SONGS] Malformed token: does not contain three parts');
        localStorage.removeItem('token');
        localStorage.removeItem('userName');
        setFilterError('Invalid token format. Please log in again.');
        navigate('/login');
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error('[EXPLORE_SONGS] Token expired:', new Date(exp).toISOString());
        localStorage.removeItem('token');
        localStorage.removeItem('userName');
        setFilterError('Session expired. Please log in again.');
        navigate('/login');
        return null;
      }
      console.log('[EXPLORE_SONGS] Token validated:', { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error('[EXPLORE_SONGS] Token validation error:', err);
      localStorage.removeItem('token');
      localStorage.removeItem('userName');
      setFilterError('Invalid token. Please log in again.');
      navigate('/login');
      return null;
    }
  }, [navigate, setFilterError]);

  useEffect(() => {
    const mediaQuery = window.matchMedia('(max-width: 767px)');
    const handleResize = () => {
      setPageSize(mediaQuery.matches ? 10 : 75);
      setPage(1);
    };
    mediaQuery.addEventListener('change', handleResize);
    return () => mediaQuery.removeEventListener('change', handleResize);
  }, []);

  const decades = ['All Decades', ...['1960s', '1970s', '1980s', '1990s', '2000s', '2010s', '2020s'].sort()];
  const popularityRanges = ['All Popularities', ...['Very Popular (80+)', 'Popular (50-79)', 'Moderate (20-49)', 'Less Popular (0-19)'].sort()];
  const requestedByOptions = ['All Requests', 'Only My Requests'];
  const statusOptions = [' Status : All', ' Status: Available', 'Status: Pending', 'Status: Unavailable'];

  useEffect(() => {
    if (liveEvents.length === 0) return;

    const token = validateToken();
    if (!token) return;

    const fetchQueues = async () => {
      const newQueues: { [eventId: number]: EventQueueItem[] } = {};
      const userName = localStorage.getItem('userName');
      if (!userName) {
        console.error('[EXPLORE_SONGS] No userName found for queue fetch');
        setQueueError('User not found. Please log in again.');
        navigate('/login');
        return;
      }

      for (const event of liveEvents) {
        try {
          const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${event.eventId}/queue`, {
            headers: { Authorization: `Bearer ${token}` },
          });
          if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Fetch queue failed for event ${event.eventId}: ${response.status} - ${errorText}`);
          }
          const data: EventQueueItem[] = await response.json();
          const uniqueQueueData = Array.from(
            new Map(data.map(item => [`${item.songId}-${item.requestorUserName}`, item])).values()
          );
          const userQueue = uniqueQueueData.filter(item => item.requestorUserName === userName);
          console.log(`Fetched queue for event ${event.eventId}:`, userQueue);
          newQueues[event.eventId] = userQueue;
        } catch (err) {
          console.error(`Fetch queue error for event ${event.eventId}:`, err);
          newQueues[event.eventId] = [];
        }
      }
      setQueues(newQueues);
    };

    fetchQueues();
  }, [liveEvents, navigate, validateToken, setQueueError]);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    console.log('Fetching favorites from:', API_ROUTES.FAVORITES);
    fetch(`${API_ROUTES.FAVORITES}`, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then(res => {
        if (!res.ok) {
          console.error(`Fetch favorites failed with status: ${res.status}`);
          throw new Error(`Fetch favorites failed: ${res.status}`);
        }
        return res.json();
      })
      .then((data: Song[]) => {
        console.log('Fetched favorites:', data);
        setFavorites(data || []);
      })
      .catch(err => {
        console.error('Fetch favorites error:', err);
        setFavorites([]);
      });
  }, [validateToken]);

  const fetchArtists = useCallback(async (retryCount: number) => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`Fetching artists from: ${API_ROUTES.ARTISTS}`);
      const response = await fetch(API_ROUTES.ARTISTS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      if (!response.ok) {
        console.error(`Fetch artists failed with status: ${response.status}, response: ${responseText}`);
        throw new Error(`Fetch artists failed with status: ${response.status}`);
      }
      const data = JSON.parse(responseText);
      const artistList = (data as string[] || []).sort();
      setArtists(['All Artists', ...artistList]);
      setArtistError(null);
    } catch (err) {
      console.error('Fetch artists error:', err);
      if (retryCount < maxRetries) {
        console.log(`Retrying artists fetch, attempt ${retryCount + 1}/${maxRetries}`);
        setTimeout(() => fetchArtists(retryCount + 1), 3000);
      } else {
        setArtists(['All Artists']);
        setArtistError('Failed to load artists after retries. Please refresh the page.');
      }
    }
  }, [validateToken]);

  const fetchGenres = useCallback(async (retryCount: number) => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`Fetching genres from: ${API_ROUTES.GENRES}`);
      const response = await fetch(API_ROUTES.GENRES, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      if (!response.ok) {
        console.error(`Fetch genres failed with status: ${response.status}, response: ${responseText}`);
        throw new Error(`Fetch genres failed with status: ${response.status}`);
      }
      const data = JSON.parse(responseText);
      const genreList = (data as string[] || []).sort();
      setGenres(['All Genres', ...genreList]);
      setGenreError(null);
    } catch (err) {
      console.error('Fetch genres error:', err);
      if (retryCount < maxRetries) {
        console.log(`Retrying genres fetch, attempt ${retryCount + 1}/${maxRetries}`);
        setTimeout(() => fetchGenres(retryCount + 1), 3000);
      } else {
        setGenres(['All Genres']);
        setGenreError('Failed to load genres after retries. Please refresh the page.');
      }
    }
  }, [validateToken]);

  const fetchSpotifySongs = useCallback(async (query: string) => {
    if (!query.trim()) {
      toast.error('Please enter a search query.');
      return;
    }
    if (!serverAvailable) {
      console.error('[FETCH_SPOTIFY] Server is not available, aborting fetch');
      toast.error('Unable to connect to the server. Please check if the server is running and try again.');
      return;
    }
    const token = validateToken();
    if (!token) return;

    console.log(`[FETCH_SPOTIFY] Fetching songs from Spotify with query: ${query}`);
    try {
      setIsSearching(true);
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(query)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[FETCH_SPOTIFY] Spotify fetch failed with status: ${response.status}, response: ${errorText}`);
        if (response.status === 401) {
          toast.error('Session expired. Please log in again.');
          localStorage.removeItem('token');
          localStorage.removeItem('userName');
          localStorage.removeItem('firstName');
          localStorage.removeItem('lastName');
          localStorage.removeItem('roles');
          navigate('/login');
          return;
        }
        throw new Error(`Spotify search failed: ${response.status} - ${errorText}`);
      }
      const data = await response.json();
      console.log('[FETCH_SPOTIFY] Spotify fetch response:', data);
      const fetchedSpotifySongs = (data.songs as SpotifySong[]) || [];
      console.log('[FETCH_SPOTIFY] Fetched Spotify songs:', fetchedSpotifySongs);
      setSpotifySongs(fetchedSpotifySongs);
      setShowSpotifyModal(true);
    } catch (err) {
      console.error('[FETCH_SPOTIFY] Spotify search error:', err);
      const errorMessage = err instanceof Error ? err.message : 'Unknown error';
      if (errorMessage.includes('ERR_CONNECTION_REFUSED')) {
        toast.error('Unable to connect to the server. Please check if the server is running and try again.');
        setServerAvailable(false);
      } else {
        toast.error('An error occurred while searching Spotify. Please try again.');
      }
      setShowSpotifyModal(true);
    } finally {
      setIsSearching(false);
    }
  }, [serverAvailable, validateToken, navigate]);

  const handleSpotifySongSelect = useCallback((song: SpotifySong) => {
    console.log('[SPOTIFY_SELECT] Selected song:', song);
    setSelectedSpotifySong(song);
    setShowSpotifyDetailsModal(true);
  }, []);

  const submitSongRequest = useCallback(async (song: SpotifySong) => {
    console.log('[SUBMIT_SONG] Submitting song request:', song);
    if (!serverAvailable) {
      console.error('[SUBMIT_SONG] Server is not available, aborting request');
      toast.error('Unable to connect to the server. Please check if the server is running and try again.');
      return;
    }
    const token = validateToken();
    if (!token) return;

    const userName = localStorage.getItem('userName');
    if (!userName) {
      console.error('[SUBMIT_SONG] No userName found in localStorage');
      toast.error('User ID missing. Please log in again.');
      navigate('/login');
      return;
    }

    const requestData = {
      title: song.title || 'Unknown Title',
      artist: song.artist || 'Unknown Artist',
      spotifyId: song.id,
      bpm: song.bpm || 0,
      danceability: song.danceability || 0,
      energy: song.energy || 0,
      valence: song.valence || null,
      popularity: song.popularity || 0,
      genre: song.genre || null,
      decade: song.decade || null,
      status: 'pending',
      requestedBy: userName,
    };

    console.log('[SUBMIT_SONG] Sending song request payload:', requestData);

    try {
      setIsSearching(true);
      const response = await fetch(API_ROUTES.REQUEST_SONG, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log(`[SUBMIT_SONGS] Song request response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`[SUBMIT_SONGS] Failed to submit song request: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          toast.error('Session expired. Please log in again.');
          localStorage.removeItem('token');
          localStorage.removeItem('userName');
          localStorage.removeItem('firstName');
          localStorage.removeItem('lastName');
          localStorage.removeItem('roles');
          navigate('/login');
          return;
        }
        if (response.status === 400 && responseText.includes('already exists')) {
          const songResponse = await fetch(`${API_ROUTES.SONG_BY_ID}?spotifyId=${encodeURIComponent(song.id)}`, {
            headers: { Authorization: `Bearer ${token}` },
          });
          if (songResponse.ok) {
            const songData = await songResponse.json();
            const status = songData.status === 'active' ? 'Available' : songData.status === 'pending' ? 'Pending' : 'Unavailable';
            setFilterError(`Song "${requestData.title}" by ${requestData.artist} already exists in the database with status: ${status}.`);
            setShowRequestConfirmationModal(false);
            setShowSpotifyModal(false);
            setShowSpotifyDetailsModal(false);
          } else {
            setFilterError(`Song "${requestData.title}" by ${requestData.artist} already exists in the database.`);
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
          console.error('[SUBMIT_SONGS] Failed to parse response as JSON:', responseText);
          throw new Error('Invalid response format from server');
        }
      }

      console.log('[SUBMIT_SONGS] Parsed response:', result);
      console.log('[SUBMIT_SONGS] Setting state: closing Spotify modal, opening confirmation');
      setRequestedSong(song);
      setShowSpotifyDetailsModal(false);
      setShowRequestConfirmationModal(true);
      toast.success('Song request submitted successfully!');
    } catch (err) {
      console.error('[SUBMIT_SONGS] Song request error:', err);
      toast.error('Failed to submit song request. Please try again.');
    } finally {
      setIsSearching(false);
    }
  }, [serverAvailable, validateToken, navigate]);

  useEffect(() => {
    fetchArtists(0);
    fetchGenres(0);
  }, [fetchArtists, fetchGenres]);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const userName = localStorage.getItem('userName');
    console.log('[EXPLORE_SONGS] Current filters:', {
      artistFilter,
      decadeFilter,
      genreFilter,
      popularityFilter,
      requestedByFilter,
      statusFilter,
      userName,
    });

    const fetchSongs = async () => {
      setIsLoading(true);
      setFilterError(null);

      // Build query parameters
      const params: { [key: string]: string } = {
        page: page.toString(),
        pageSize: pageSize.toString(),
      };
      if (statusFilter !== ' Status : All') {
        let statusValue = statusFilter.replace(/\s*status\s*:\s*/i, '').trim().toLowerCase();
        if (statusValue === 'available') {
          statusValue = 'active';
        }
        params.status = statusValue;
      }
      if (artistFilter !== 'All Artists') {
        params.artist = artistFilter;
      }
      if (decadeFilter !== 'All Decades') {
        params.decade = decadeFilter;
      }
      if (genreFilter !== 'All Genres') {
        params.genre = genreFilter;
      }
      if (popularityFilter !== 'All Popularities') {
        switch (popularityFilter) {
          case 'Very Popular (80+)':
            params.popularity = 'veryPopular';
            break;
          case 'Popular (50-79)':
            params.popularity = 'popular';
            break;
          case 'Moderate (20-49)':
            params.popularity = 'moderate';
            break;
          case 'Less Popular (0-19)':
            params.popularity = 'lessPopular';
            break;
        }
      }
      if (requestedByFilter === 'Only My Requests' && userName) {
        params.requestedBy = userName;
      }

      const queryString = new URLSearchParams(params).toString();
      const url = `${API_ROUTES.EXPLORE_SONGS}?${queryString}`;
      console.log('[EXPLORE_SONGS] Fetching songs with URL:', url);

      try {
        const response = await fetch(url, {
          headers: { Authorization: `Bearer ${token}` },
        });

        if (!response.ok) {
          const errorData = await response.json();
          console.error(`[EXPLORE_SONGS] Fetch failed with status: ${response.status}, response:`, errorData);
          if (response.status === 401) {
            setFilterError('Session expired. Please log in again.');
            localStorage.removeItem('token');
            localStorage.removeItem('userName');
            navigate('/login');
            return;
          }
          if (response.status === 400) {
            setFilterError(errorData.error || 'Invalid filter parameters.');
            setBrowseSongs([]);
            setTotalPages(1);
            setIsLoading(false);
            return;
          }
          throw new Error(`Fetch failed: ${response.status} - ${errorData.error || 'Unknown error'}`);
        }

        const data = await response.json();
        console.log('[EXPLORE_SONGS] Fetched songs response:', data);

        const songs: Song[] = data.songs || [];
        const totalCount: number = data.totalCount || 0;
        console.log('[EXPLORE_SONGS] Songs fetched:', songs.length, 'IDs:', songs.map(song => song.id), 'Total count:', totalCount);

        // Log unique values for debugging
        const uniqueStatuses = [...new Set(songs.map(song => song.status))];
        const uniqueArtists = [...new Set(songs.map(song => song.artist))];
        const uniqueDecades = [...new Set(songs.map(song => song.decade))];
        const uniqueGenres = [...new Set(songs.map(song => song.genre))];
        console.log('[EXPLORE_SONGS] Unique status values:', uniqueStatuses);
        console.log('[EXPLORE_SONGS] Unique artist values:', uniqueArtists);
        console.log('[EXPLORE_SONGS] Unique decade values:', uniqueDecades);
        console.log('[EXPLORE_SONGS] Unique genre values:', uniqueGenres);

        // Validate song data
        const invalidSongs = songs.filter(song => 
          !song.id || !song.title || !song.artist || !song.status || 
          typeof song.id !== 'number' || typeof song.title !== 'string' || 
          typeof song.artist !== 'string' || typeof song.status !== 'string'
        );
        if (invalidSongs.length > 0) {
          console.warn('[EXPLORE_SONGS] Invalid songs:', invalidSongs.map(song => ({
            id: song.id,
            title: song.title,
            artist: song.artist,
            status: song.status,
            decade: song.decade,
            genre: song.genre,
          })));
        }

        setBrowseSongs(songs);
        setTotalPages(Math.ceil(totalCount / pageSize) || 1);
        if (songs.length === 0 && totalCount === 0) {
          setFilterError('No songs found with the current filters. Try resetting filters or checking the database.');
        }
        setIsLoading(false);
      } catch (err) {
        console.error('[EXPLORE_SONGS] Fetch error:', err);
        setBrowseSongs([]);
        setFilterError('Failed to load songs. Please try again or check server status.');
        setTotalPages(1);
        setIsLoading(false);
      }
    };

    fetchSongs();
  }, [artistFilter, decadeFilter, genreFilter, popularityFilter, requestedByFilter, statusFilter, page, pageSize, navigate, validateToken]);

  const toggleFavorite = async (song: Song) => {
    if (song.status?.toLowerCase() !== 'active') {
      toast.error('Only Available songs can be added to favorites.');
      return;
    }
    const token = validateToken();
    if (!token) return;

    const isFavorite = favorites.some(fav => fav.id === song.id);
    const method = isFavorite ? 'DELETE' : 'POST';
    const url = isFavorite ? `${API_ROUTES.FAVORITES}/${song.id}` : API_ROUTES.FAVORITES;

    console.log(`[EXPLORE_SONGS] Toggling favorite for song ${song.id}, isFavorite: ${isFavorite}, method: ${method}, url: ${url}`);

    try {
      const response = await fetch(url, {
        method,
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: method === 'POST' ? JSON.stringify({ songId: song.id }) : undefined,
      });

      const responseText = await response.text();
      console.log(`[EXPLORE_SONGS] Toggle favorite response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`[EXPLORE_SONGS] Failed to ${isFavorite ? 'remove' : 'add'} favorite: ${response.status} - ${responseText}`);
        throw new Error(`${isFavorite ? 'Remove' : 'Add'} favorite failed: ${response.status}`);
      }

      let result;
      try {
        result = JSON.parse(responseText);
      } catch (error) {
        console.error('[EXPLORE_SONGS] Failed to parse response as JSON:', responseText);
        throw new Error('Invalid response format from server');
      }

      console.log(`[EXPLORE_SONGS] Parsed toggle favorite response:`, result);

      if (result.success) {
        const updatedFavorites = isFavorite
          ? favorites.filter(fav => fav.id !== song.id)
          : [...favorites, { ...song }];
        console.log(`[EXPLORE_SONGS] Updated favorites after ${isFavorite ? 'removal' : 'addition'}:`, updatedFavorites);
        setFavorites([...updatedFavorites]);
        toast.success(`Song ${isFavorite ? 'removed from' : 'added to'} favorites!`);
      } else {
        console.error('[EXPLORE_SONGS] Toggle favorite failed: Success flag not set in response');
        toast.error(`Failed to ${isFavorite ? 'remove' : 'add'} favorite. Please try again.`);
      }
    } catch (err) {
      console.error(`[EXPLORE_SONGS] ${isFavorite ? 'Remove' : 'Add'} favorite error:`, err);
      toast.error('Failed to update favorites. Please try again.');
    }
  };

  const addToEventQueue = async (song: Song, eventId: number): Promise<void> => {
    if (song.status?.toLowerCase() !== 'active') {
      toast.error('Only Available songs can be added to the queue.');
      return;
    }
    const token = validateToken();
    const requestorUserName = localStorage.getItem('userName');
    console.log('[EXPLORE_SONGS] addToEventQueue - token:', token ? token.slice(0, 10) : null, '...', 'requestorUserName:', requestorUserName);

    if (!token) {
      setQueueError('Authentication token missing. Please log in again.');
      throw new Error('Authentication token missing. Please log in again.');
    }

    if (!requestorUserName) {
      console.error('[EXPLORE_SONGS] Invalid or missing requestorUserName in addToEventQueue');
      setQueueError('User not found. Please log in again to add songs to the queue.');
      throw new Error('User not found. Please log in again to add songs to the queue.');
    }

    const event = liveEvents.find(e => e.eventId === eventId);
    if (!event) {
      console.error('[EXPLORE_SONGS] Event not found for eventId:', eventId);
      setQueueError('Selected event not found.');
      throw new Error('Selected event not found.');
    }

    if (!checkedIn && event.status.toLowerCase() === 'live') {
      console.error('[EXPLORE_SONGS] User not checked in for live event:', eventId);
      setQueueError('You must be checked into the live event to add to its queue.');
      throw new Error('User not checked into live event.');
    }

    const queueForEvent = queues[eventId] || [];
    const isInQueue = queueForEvent.some(q => q.songId === song.id);
    if (isInQueue) {
      console.log(`[EXPLORE_SONGS] Song ${song.id} is already in the queue for event ${eventId}`);
      setQueueError(`Song "${song.title}" is already in the queue for this event.`);
      return;
    }

    try {
      const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${eventId}/queue`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          songId: song.id,
          requestorUserName: requestorUserName,
        }),
      });

      const responseText = await response.text();
      console.log(`[EXPLORE_SONGS] Add to queue response for event ${eventId}: status=${response.status}, body=${responseText}`);

      if (!response.ok) {
        console.error(`[EXPLORE_SONGS] Failed to add song to queue for event ${eventId}: ${response.status} - ${responseText}`);
        throw new Error(`Add to queue failed: ${responseText || response.statusText}`);
      }

      const newQueueItem: EventQueueItem = JSON.parse(responseText);
      console.log(`[EXPLORE_SONGS] Added to queue for event ${eventId}:`, newQueueItem);
      setQueues(prev => ({
        ...prev,
        [eventId]: [...(prev[eventId] || []), newQueueItem],
      }));
      setQueueError(null);
      toast.success('Song added to queue successfully!');
    } catch (err) {
      console.error('[EXPLORE_SONGS] Add to queue error:', err);
      setQueueError(err instanceof Error ? err.message : 'Failed to add song to queue.');
      throw err;
    }
  };

  const handleFilterSelect = (type: string, value: string) => {
    if (type === 'Artist') setArtistFilter(value);
    if (type === 'Decade') setDecadeFilter(value);
    if (type === 'Genre') setGenreFilter(value);
    if (type === 'Popularity') setPopularityFilter(value);
    if (type === 'RequestedBy') setRequestedByFilter(value);
    if (type === 'Status') setStatusFilter(value);
    setPage(1);
    setBrowseSongs([]);
    setShowFilterDropdown(null);
  };

  const resetFilter = (type: string) => {
    if (type === 'Artist') setArtistFilter('All Artists');
    if (type === 'Decade') setDecadeFilter('All Decades');
    if (type === 'Genre') setGenreFilter('All Genres');
    if (type === 'Popularity') setPopularityFilter('All Popularities');
    if (type === 'RequestedBy') setRequestedByFilter('All Requests');
    if (type === 'Status') setStatusFilter(' Status : All');
    setPage(1);
    setBrowseSongs([]);
  };

  const resetAllFilters = () => {
    setArtistFilter('All Artists');
    setDecadeFilter('All Decades');
    setGenreFilter('All Genres');
    setPopularityFilter('All Popularities');
    setRequestedByFilter('All Requests');
    setStatusFilter(' Status : All');
    setPage(1);
    setBrowseSongs([]);
  };

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
  };

  const handleRequestNewSong = (e: React.MouseEvent<HTMLButtonElement> | React.TouchEvent<HTMLButtonElement>) => {
    e.preventDefault();
    setSpotifySearchQuery('');
    setSpotifySongs([]);
    setShowSpotifyModal(true);
  };

  const handleSpotifySearch = () => {
    if (spotifySearchQuery.trim()) {
      fetchSpotifySongs(spotifySearchQuery);
    } else {
      toast.error('Please enter a search query.');
    }
  };

  const handleSpotifySearchKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      console.log('[SPOTIFY_SEARCH] Enter key pressed with query:', spotifySearchQuery);
      handleSpotifySearch();
    }
  };

  try {
    return (
      <div className="explore-songs mobile-explore-songs">
        <header className="explore-header">
          <h1>Explore Songs</h1>
          <div className="header-buttons">
            <button
              onClick={resetAllFilters}
              onTouchEnd={resetAllFilters}
              className="reset-button"
            >
              Reset All
            </button>
            <button
              onClick={handleRequestNewSong}
              onTouchEnd={handleRequestNewSong}
              className="request-song-button"
            >
              Request New Song
            </button>
            <button
              onClick={() => navigate('/dashboard')}
              onTouchEnd={() => navigate('/dashboard')}
              className="back-button"
            >
              Back to Dashboard
            </button>
          </div>
        </header>

        <section className="browse-section">
          {artistError && <p className="error-message">{artistError}</p>}
          {genreError && <p className="error-message">{genreError}</p>}
          {queueError && <p className="error-message">{queueError}</p>}
          {filterError && <p className="error-message">{filterError}</p>}
          <div className="filter-tabs">
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={artistFilter !== 'All Artists' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'Artist' ? null : 'Artist')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'Artist' ? null : 'Artist')}
                >
                  {artistFilter} v
                </button>
                {artistFilter !== 'All Artists' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('Artist')}
                    onTouchEnd={() => resetFilter('Artist')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'Artist' && (
                <div className="filter-dropdown">
                  {artists.map(artist => (
                    <button
                      key={artist}
                      onClick={() => handleFilterSelect('Artist', artist)}
                      onTouchEnd={() => handleFilterSelect('Artist', artist)}
                    >
                      {artist}
                    </button>
                  ))}
                </div>
              )}
            </div>
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={decadeFilter !== 'All Decades' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'Decade' ? null : 'Decade')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'Decade' ? null : 'Decade')}
                >
                  {decadeFilter} v
                </button>
                {decadeFilter !== 'All Decades' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('Decade')}
                    onTouchEnd={() => resetFilter('Decade')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'Decade' && (
                <div className="filter-dropdown">
                  {decades.map(decade => (
                    <button
                      key={decade}
                      onClick={() => handleFilterSelect('Decade', decade)}
                      onTouchEnd={() => handleFilterSelect('Decade', decade)}
                    >
                      {decade}
                    </button>
                  ))}
                </div>
              )}
            </div>
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={genreFilter !== 'All Genres' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'Genre' ? null : 'Genre')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'Genre' ? null : 'Genre')}
                >
                  {genreFilter} v
                </button>
                {genreFilter !== 'All Genres' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('Genre')}
                    onTouchEnd={() => resetFilter('Genre')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'Genre' && (
                <div className="filter-dropdown">
                  {genres.map(genre => (
                    <button
                      key={genre}
                      onClick={() => handleFilterSelect('Genre', genre)}
                      onTouchEnd={() => handleFilterSelect('Genre', genre)}
                    >
                      {genre}
                    </button>
                  ))}
                </div>
              )}
            </div>
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={popularityFilter !== 'All Popularities' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'Popularity' ? null : 'Popularity')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'Popularity' ? null : 'Popularity')}
                >
                  {popularityFilter} v
                </button>
                {popularityFilter !== 'All Popularities' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('Popularity')}
                    onTouchEnd={() => resetFilter('Popularity')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'Popularity' && (
                <div className="filter-dropdown">
                  {popularityRanges.map(range => (
                    <button
                      key={range}
                      onClick={() => handleFilterSelect('Popularity', range)}
                      onTouchEnd={() => handleFilterSelect('Popularity', range)}
                    >
                      {range}
                    </button>
                  ))}
                </div>
              )}
            </div>
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={requestedByFilter !== 'All Requests' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'RequestedBy' ? null : 'RequestedBy')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'RequestedBy' ? null : 'RequestedBy')}
                >
                  {requestedByFilter} v
                </button>
                {requestedByFilter !== 'All Requests' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('RequestedBy')}
                    onTouchEnd={() => resetFilter('RequestedBy')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'RequestedBy' && (
                <div className="filter-dropdown">
                  {requestedByOptions.map(option => (
                    <button
                      key={option}
                      onClick={() => handleFilterSelect('RequestedBy', option)}
                      onTouchEnd={() => handleFilterSelect('RequestedBy', option)}
                    >
                      {option}
                    </button>
                  ))}
                </div>
              )}
            </div>
            <div className="filter-tab">
              <div className="filter-tab-header">
                <button
                  className={statusFilter !== ' Status : All' ? 'active' : ''}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === 'Status' ? null : 'Status')}
                  onTouchEnd={() => setShowFilterDropdown(showFilterDropdown === 'Status' ? null : 'Status')}
                >
                  {statusFilter} v
                </button>
                {statusFilter !== ' Status : All' && (
                  <button
                    className="reset-filter"
                    onClick={() => resetFilter('Status')}
                    onTouchEnd={() => resetFilter('Status')}
                  >
                    x
                  </button>
                )}
              </div>
              {showFilterDropdown === 'Status' && (
                <div className="filter-dropdown">
                  {statusOptions.map(option => (
                    <button
                      key={option}
                      onClick={() => handleFilterSelect('Status', option)}
                      onTouchEnd={() => handleFilterSelect('Status', option)}
                    >
                      {option}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
          <div className="song-grid">
            {isLoading ? (
              <p>Loading...</p>
            ) : browseSongs.length === 0 ? (
              <p>No songs found</p>
            ) : (
              browseSongs.map(song => (
                <div key={song.id} className="song-card">
                  <div
                    className="song-info"
                    onClick={() => setSelectedSong(song)}
                    onTouchEnd={() => setSelectedSong(song)}
                  >
                    <div className="song-title">{song.title}</div>
                    <div className="song-artist">({song.artist || 'Unknown Artist'})</div>
                    <div className="song-status">
                      {song.status?.toLowerCase() === 'active' && (
                        <span className="song-status-badge available">Available</span>
                      )}
                      {song.status?.toLowerCase() === 'pending' && (
                        <span className="song-status-badge pending">Pending</span>
                      )}
                      {song.status?.toLowerCase() === 'unavailable' && (
                        <span className="song-status-badge unavailable">Unavailable</span>
                      )}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>
          {totalPages > 1 && (
            <div className="pagination">
              <button
                className="pagination-button"
                disabled={page === 1}
                onClick={() => handlePageChange(page - 1)}
                onTouchEnd={() => handlePageChange(page - 1)}
              >
                Previous
              </button>
              <span>Page {page} of {totalPages}</span>
              <button
                className="pagination-button"
                disabled={page === totalPages}
                onClick={() => handlePageChange(page + 1)}
                onTouchEnd={() => handlePageChange(page + 1)}
              >
                Next
              </button>
            </div>
          )}
        </section>

        {showSpotifyModal && (
          <div className="modal-overlay secondary-modal mobile-spotify-modal">
            <div className="modal-content spotify-modal">
              <h2 className="modal-title">Request a New Song</h2>
              <div className="search-bar-container">
                <input
                  type="text"
                  placeholder="Search for a song or artist"
                  value={spotifySearchQuery}
                  onChange={(e) => setSpotifySearchQuery(e.target.value)}
                  onKeyDown={handleSpotifySearchKeyDown}
                  className="search-bar"
                  aria-label="Search for Spotify songs"
                  disabled={isSearching}
                />
                <button
                  onClick={handleSpotifySearch}
                  onTouchEnd={handleSpotifySearch}
                  className="search-button"
                  aria-label="Search Spotify"
                  disabled={isSearching}
                >
                  {isSearching ? <LoadingOutlined style={{ fontSize: '24px' }} /> : <SearchOutlined style={{ fontSize: '24px' }} />}
                </button>
                <button
                  onClick={() => {
                    setSpotifySearchQuery('');
                    setSpotifySongs([]);
                  }}
                  onTouchEnd={() => {
                    setSpotifySearchQuery('');
                    setSpotifySongs([]);
                  }}
                  className="reset-button"
                  aria-label="Reset search"
                  disabled={isSearching}
                >
                  <CloseOutlined style={{ fontSize: '24px' }} />
                </button>
              </div>
              {isSearching ? (
                <p className="modal-text">Searching...</p>
              ) : spotifySongs.length === 0 ? (
                <p className="modal-text">No songs found on Spotify. Try a different search.</p>
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

        {selectedSong && (
          <SongDetailsModal
            song={selectedSong}
            isFavorite={favorites.some(fav => fav.id === selectedSong.id)}
            isInQueue={currentEvent ? queues[currentEvent.eventId]?.some(q => q.songId === selectedSong.id) || false : false}
            onClose={() => setSelectedSong(null)}
            onToggleFavorite={selectedSong.status?.toLowerCase() === 'active' ? toggleFavorite : undefined}
            onAddToQueue={selectedSong.status?.toLowerCase() === 'active' ? addToEventQueue : undefined}
            eventId={currentEvent?.eventId}
            checkedIn={checkedIn}
            isCurrentEventLive={isCurrentEventLive}
          />
        )}
        <Modals
          isSearching={isSearching}
          searchError={filterError}
          songs={browseSongs}
          spotifySongs={spotifySongs}
          selectedSpotifySong={selectedSpotifySong}
          requestedSong={requestedSong}
          selectedSong={selectedSong}
          showSearchModal={false}
          showSpotifyModal={false} // Handled directly in this component
          showSpotifyDetailsModal={showSpotifyDetailsModal}
          showRequestConfirmationModal={showRequestConfirmationModal}
          showReorderErrorModal={false}
          reorderError={null}
          fetchSpotifySongs={fetchSpotifySongs}
          handleSpotifySongSelect={handleSpotifySongSelect}
          submitSongRequest={submitSongRequest}
          resetSearch={() => {
            setSpotifySongs([]);
            setSelectedSpotifySong(null);
            setShowSpotifyModal(false);
            setShowSpotifyDetailsModal(false);
            setShowRequestConfirmationModal(false);
            setRequestedSong(null);
            setSpotifySearchQuery('');
          }}
          setSelectedSong={setSelectedSong}
          setShowSpotifyModal={setShowSpotifyModal}
          setShowSpotifyDetailsModal={setShowSpotifyDetailsModal}
          setShowRequestConfirmationModal={setShowRequestConfirmationModal}
          setRequestedSong={setRequestedSong}
          setSearchError={setFilterError}
          favorites={favorites}
          myQueues={queues}
          toggleFavorite={toggleFavorite}
          addToEventQueue={addToEventQueue}
          currentEvent={currentEvent}
          checkedIn={checkedIn}
          isCurrentEventLive={isCurrentEventLive}
          selectedQueueId={undefined}
          requestNewSong={fetchSpotifySongs}
        />
      </div>
    );
  } catch (error) {
    console.error('[EXPLORE_SONGS] Render error:', error);
    return <div>Error in ExploreSongs: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default ExploreSongs;