// src/pages/ExploreSongs.tsx
import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { API_ROUTES } from '../config/apiConfig';
import SongDetailsModal from '../components/SongDetailsModal';
import './ExploreSongs.css';
import { Song, EventQueueItem, Event } from '../types';
import { useEventContext } from "../context/EventContext";

const ExploreSongs: React.FC = () => {
  const navigate = useNavigate();
  const { checkedIn, isCurrentEventLive, currentEvent, liveEvents } = useEventContext();
  const [queues, setQueues] = useState<{ [eventId: number]: EventQueueItem[] }>({});
  const [favorites, setFavorites] = useState<Song[]>([]);
  const [artistFilter, setArtistFilter] = useState<string>("All Artists");
  const [decadeFilter, setDecadeFilter] = useState<string>("All Decades");
  const [genreFilter, setGenreFilter] = useState<string>("All Genres");
  const [popularityFilter, setPopularityFilter] = useState<string>("All Popularities");
  const [requestedByFilter, setRequestedByFilter] = useState<string>("All Requests");
  const [showFilterDropdown, setShowFilterDropdown] = useState<string | null>(null);
  const [browseSongs, setBrowseSongs] = useState<Song[]>([]);
  const [page, setPage] = useState<number>(1);
  const [pageSize] = useState<number>(50);
  const [totalPages, setTotalPages] = useState<number>(1);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [selectedSong, setSelectedSong] = useState<Song | null>(null);
  const [artists, setArtists] = useState<string[]>(["All Artists"]);
  const [genres, setGenres] = useState<string[]>(["All Genres"]);
  const [artistError, setArtistError] = useState<string | null>(null);
  const [genreError, setGenreError] = useState<string | null>(null);
  const [queueError, setQueueError] = useState<string | null>(null);
  const [filterError, setFilterError] = useState<string | null>(null);
  const maxRetries = 3;

  // Static data for filters
  const decades = ["All Decades", ...["1960s", "1970s", "1980s", "1990s", "2000s", "2010s", "2020s"].sort()];
  const popularityRanges = ["All Popularities", ...["Very Popular (80+)", "Popular (50-79)", "Moderate (20-49)", "Less Popular (0-19)"].sort()];
  const requestedByOptions = ["All Requests", "Only My Requests"];

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[EXPLORE_SONGS] No token or userName found");
      setFilterError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[EXPLORE_SONGS] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFilterError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[EXPLORE_SONGS] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFilterError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[EXPLORE_SONGS] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[EXPLORE_SONGS] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setFilterError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  // Fetch queues for all live events
  useEffect(() => {
    if (liveEvents.length === 0) return;

    const token = validateToken();
    if (!token) return;

    const fetchQueues = async () => {
      const newQueues: { [eventId: number]: EventQueueItem[] } = {};
      const userName = localStorage.getItem("userName");
      if (!userName) {
        console.error("[EXPLORE_SONGS] No userName found for queue fetch");
        setQueueError("User not found. Please log in again.");
        navigate("/login");
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
            new Map(
              data.map(item => [`${item.songId}-${item.requestorUserName}`, item])
            ).values()
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
  }, [liveEvents, navigate]);

  // Fetch favorites on mount
  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    console.log("Fetching favorites from:", API_ROUTES.FAVORITES);
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
        console.log("Fetched favorites:", data);
        setFavorites(data || []);
      })
      .catch(err => {
        console.error("Fetch favorites error:", err);
        setFavorites([]);
      });
  }, []);

  // Fetch artists with retry mechanism
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
      setArtists(["All Artists", ...artistList]);
      setArtistError(null);
    } catch (err) {
      console.error("Fetch artists error:", err);
      if (retryCount < maxRetries) {
        console.log(`Retrying artists fetch, attempt ${retryCount + 1}/${maxRetries}`);
        setTimeout(() => fetchArtists(retryCount + 1), 3000);
      } else {
        setArtists(["All Artists"]);
        setArtistError("Failed to load artists after retries. Please refresh the page.");
      }
    }
  }, []);

  // Fetch genres with retry mechanism
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
      setGenres(["All Genres", ...genreList]);
      setGenreError(null);
    } catch (err) {
      console.error("Fetch genres error:", err);
      if (retryCount < maxRetries) {
        console.log(`Retrying genres fetch, attempt ${retryCount + 1}/${maxRetries}`);
        setTimeout(() => fetchGenres(retryCount + 1), 3000);
      } else {
        setGenres(["All Genres"]);
        setGenreError("Failed to load genres after retries. Please refresh the page.");
      }
    }
  }, []);

  // Fetch artists and genres on mount
  useEffect(() => {
    fetchArtists(0);
    fetchGenres(0);
  }, [fetchArtists, fetchGenres]);

  // Fetch songs based on all active filters
  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const userName = localStorage.getItem("userName");
    console.log("[EXPLORE_SONGS] Current filters:", {
      artistFilter,
      decadeFilter,
      genreFilter,
      popularityFilter,
      requestedByFilter,
      userName
    });

    const queryParams: string[] = [];
    if (artistFilter !== "All Artists") {
      queryParams.push(`artist=${encodeURIComponent(artistFilter)}`);
    }
    if (decadeFilter !== "All Decades") {
      queryParams.push(`decade=${encodeURIComponent(decadeFilter.toLowerCase())}`);
    }
    if (genreFilter !== "All Genres") {
      queryParams.push(`genre=${encodeURIComponent(genreFilter)}`);
    }
    if (popularityFilter !== "All Popularities") {
      let sortParam = "";
      switch (popularityFilter) {
        case "Very Popular (80+)":
          sortParam = "popularity=80-100";
          break;
        case "Popular (50-79)":
          sortParam = "popularity=50-79";
          break;
        case "Moderate (20-49)":
          sortParam = "popularity=20-49";
          break;
        case "Less Popular (0-19)":
          sortParam = "popularity=0-19";
          break;
        default:
          sortParam = "popularity";
      }
      queryParams.push(sortParam);
    }
    if (requestedByFilter === "Only My Requests" && userName) {
      queryParams.push(`requestedBy=${encodeURIComponent(userName)}`);
    }

    const queryString = queryParams.length > 0 ? queryParams.join('&') : "query=all";
    const url = `${API_ROUTES.SONGS_SEARCH}?${queryString}&sort=title&page=${page}&pageSize=${pageSize}`;
    console.log("[EXPLORE_SONGS] Fetching songs with URL:", url);

    setIsLoading(true);
    setFilterError(null);
    fetch(url, {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then(res => {
        if (!res.ok) {
          const errorText = res.text();
          throw new Error(`Browse failed: ${res.status} - ${errorText}`);
        }
        return res.json();
      })
      .then(data => {
        console.log("[EXPLORE_SONGS] Fetched songs response:", data);
        const newSongs = ((data.songs as Song[]) || []).filter(song => song.status?.toLowerCase() === 'active');
        console.log("[EXPLORE_SONGS] Filtered active songs:", newSongs);
        if (requestedByFilter === "Only My Requests" && newSongs.length > 0 && userName) {
          const unfiltered = newSongs.some(song => song.requestedBy !== userName);
          if (unfiltered) {
            setFilterError("Unexpected songs returned. The filter may not be applied correctly.");
          }
        }
        if (requestedByFilter === "Only My Requests" && newSongs.length === 0) {
          setFilterError("No songs found for your requests.");
        }
        newSongs.sort((a, b) => a.title.localeCompare(b.title));
        setBrowseSongs(newSongs);
        setTotalPages(data.totalPages || 1);
        setIsLoading(false);
      })
      .catch(err => {
        console.error("[EXPLORE_SONGS] Browse error:", err);
        setBrowseSongs([]);
        setFilterError("Failed to load songs. Please try again.");
        setIsLoading(false);
      });
  }, [artistFilter, decadeFilter, genreFilter, popularityFilter, requestedByFilter, page, pageSize, navigate]);

  const toggleFavorite = async (song: Song) => {
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
          'Authorization': `Bearer ${token}`,
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
        console.error("[EXPLORE_SONGS] Failed to parse response as JSON:", responseText);
        throw new Error("Invalid response format from server");
      }

      console.log(`[EXPLORE_SONGS] Parsed toggle favorite response:`, result);

      if (result.success) {
        const updatedFavorites = isFavorite
          ? favorites.filter(fav => fav.id !== song.id)
          : [...favorites, { ...song }];
        console.log(`[EXPLORE_SONGS] Updated favorites after ${isFavorite ? 'removal' : 'addition'}:`, updatedFavorites);
        setFavorites([...updatedFavorites]);
      } else {
        console.error("[EXPLORE_SONGS] Toggle favorite failed: Success flag not set in response");
      }
    } catch (err) {
      console.error(`[EXPLORE_SONGS] ${isFavorite ? 'Remove' : 'Add'} favorite error:`, err);
    }
  };

  const addToEventQueue = async (song: Song, eventId: number): Promise<void> => {
    const token = validateToken();
    const requestorUserName = localStorage.getItem("userName");
    console.log("[EXPLORE_SONGS] addToEventQueue - token:", token ? token.slice(0, 10) : null, "...", "requestorUserName:", requestorUserName);

    if (!token) {
      setQueueError("Authentication token missing. Please log in again.");
      throw new Error("Authentication token missing. Please log in again.");
    }

    if (!requestorUserName) {
      console.error("[EXPLORE_SONGS] Invalid or missing requestorUserName in addToEventQueue");
      setQueueError("User not found. Please log in again to add songs to the queue.");
      throw new Error("User not found. Please log in again to add songs to the queue.");
    }

    const event = liveEvents.find(e => e.eventId === eventId);
    if (!event) {
      console.error("[EXPLORE_SONGS] Event not found for eventId:", eventId);
      setQueueError("Selected event not found.");
      throw new Error("Selected event not found.");
    }

    if (!checkedIn && event.status.toLowerCase() === "live") {
      console.error("[EXPLORE_SONGS] User not checked in for live event:", eventId);
      setQueueError("You must be checked into the live event to add to its queue.");
      throw new Error("User not checked into live event.");
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
          'Authorization': `Bearer ${token}`,
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
    } catch (err) {
      console.error("[EXPLORE_SONGS] Add to queue error:", err);
      setQueueError(err instanceof Error ? err.message : "Failed to add song to queue.");
      throw err;
    }
  };

  const handleFilterSelect = (type: string, value: string) => {
    if (type === "Artist") setArtistFilter(value);
    if (type === "Decade") setDecadeFilter(value);
    if (type === "Genre") setGenreFilter(value);
    if (type === "Popularity") setPopularityFilter(value);
    if (type === "RequestedBy") setRequestedByFilter(value);
    setPage(1);
    setBrowseSongs([]);
    setShowFilterDropdown(null);
  };

  const resetFilter = (type: string) => {
    if (type === "Artist") setArtistFilter("All Artists");
    if (type === "Decade") setDecadeFilter("All Decades");
    if (type === "Genre") setGenreFilter("All Genres");
    if (type === "Popularity") setPopularityFilter("All Popularities");
    if (type === "RequestedBy") setRequestedByFilter("All Requests");
    setPage(1);
    setBrowseSongs([]);
  };

  const resetAllFilters = () => {
    setArtistFilter("All Artists");
    setDecadeFilter("All Decades");
    setGenreFilter("All Genres");
    setPopularityFilter("All Popularities");
    setRequestedByFilter("All Requests");
    setPage(1);
    setBrowseSongs([]);
  };

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
  };

  try {
    return (
      <div className="explore-songs mobile-explore-songs">
        <header className="explore-header">
          <h1>Explore Songs</h1>
          <div className="header-buttons">
            <button 
              onClick={resetAllFilters} 
              onTouchStart={resetAllFilters} 
              className="reset-button"
            >
              Reset All
            </button>
            <button 
              onClick={() => navigate('/dashboard')} 
              onTouchStart={() => navigate('/dashboard')} 
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
                  className={artistFilter !== "All Artists" ? "active" : ""}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === "Artist" ? null : "Artist")}
                  onTouchStart={() => setShowFilterDropdown(showFilterDropdown === "Artist" ? null : "Artist")}
                >
                  {artistFilter} ▼
                </button>
                {artistFilter !== "All Artists" && (
                  <button 
                    className="reset-filter" 
                    onClick={() => resetFilter("Artist")}
                    onTouchStart={() => resetFilter("Artist")}
                  >
                    ✕
                  </button>
                )}
              </div>
              {showFilterDropdown === "Artist" && (
                <div className="filter-dropdown">
                  {artists.map(artist => (
                    <button
                      key={artist}
                      onClick={() => handleFilterSelect("Artist", artist)}
                      onTouchStart={() => handleFilterSelect("Artist", artist)}
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
                  className={decadeFilter !== "All Decades" ? "active" : ""}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === "Decade" ? null : "Decade")}
                  onTouchStart={() => setShowFilterDropdown(showFilterDropdown === "Decade" ? null : "Decade")}
                >
                  {decadeFilter} ▼
                </button>
                {decadeFilter !== "All Decades" && (
                  <button 
                    className="reset-filter" 
                    onClick={() => resetFilter("Decade")}
                    onTouchStart={() => resetFilter("Decade")}
                  >
                    ✕
                  </button>
                )}
              </div>
              {showFilterDropdown === "Decade" && (
                <div className="filter-dropdown">
                  {decades.map(decade => (
                    <button
                      key={decade}
                      onClick={() => handleFilterSelect("Decade", decade)}
                      onTouchStart={() => handleFilterSelect("Decade", decade)}
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
                  className={genreFilter !== "All Genres" ? "active" : ""}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === "Genre" ? null : "Genre")}
                  onTouchStart={() => setShowFilterDropdown(showFilterDropdown === "Genre" ? null : "Genre")}
                >
                  {genreFilter} ▼
                </button>
                {genreFilter !== "All Genres" && (
                  <button 
                    className="reset-filter" 
                    onClick={() => resetFilter("Genre")}
                    onTouchStart={() => resetFilter("Genre")}
                  >
                    ✕
                  </button>
                )}
              </div>
              {showFilterDropdown === "Genre" && (
                <div className="filter-dropdown">
                  {genres.map(genre => (
                    <button
                      key={genre}
                      onClick={() => handleFilterSelect("Genre", genre)}
                      onTouchStart={() => handleFilterSelect("Genre", genre)}
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
                  className={popularityFilter !== "All Popularities" ? "active" : ""}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === "Popularity" ? null : "Popularity")}
                  onTouchStart={() => setShowFilterDropdown(showFilterDropdown === "Popularity" ? null : "Popularity")}
                >
                  {popularityFilter} ▼
                </button>
                {popularityFilter !== "All Popularities" && (
                  <button 
                    className="reset-filter" 
                    onClick={() => resetFilter("Popularity")}
                    onTouchStart={() => resetFilter("Popularity")}
                  >
                    ✕
                  </button>
                )}
              </div>
              {showFilterDropdown === "Popularity" && (
                <div className="filter-dropdown">
                  {popularityRanges.map(range => (
                    <button
                      key={range}
                      onClick={() => handleFilterSelect("Popularity", range)}
                      onTouchStart={() => handleFilterSelect("Popularity", range)}
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
                  className={requestedByFilter !== "All Requests" ? "active" : ""}
                  onClick={() => setShowFilterDropdown(showFilterDropdown === "RequestedBy" ? null : "RequestedBy")}
                  onTouchStart={() => setShowFilterDropdown(showFilterDropdown === "RequestedBy" ? null : "RequestedBy")}
                >
                  {requestedByFilter} ▼
                </button>
                {requestedByFilter !== "All Requests" && (
                  <button 
                    className="reset-filter" 
                    onClick={() => resetFilter("RequestedBy")}
                    onTouchStart={() => resetFilter("RequestedBy")}
                  >
                    ✕
                  </button>
                )}
              </div>
              {showFilterDropdown === "RequestedBy" && (
                <div className="filter-dropdown">
                  {requestedByOptions.map(option => (
                    <button
                      key={option}
                      onClick={() => handleFilterSelect("RequestedBy", option)}
                      onTouchStart={() => handleFilterSelect("RequestedBy", option)}
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
                    onTouchStart={() => setSelectedSong(song)}
                  >
                    <span>{song.title} - {song.artist}</span>
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
                onTouchStart={() => handlePageChange(page - 1)}
              >
                Previous
              </button>
              <span>Page {page} of {totalPages}</span>
              <button
                className="pagination-button"
                disabled={page === totalPages}
                onClick={() => handlePageChange(page + 1)}
                onTouchStart={() => handlePageChange(page + 1)}
              >
                Next
              </button>
            </div>
          )}
        </section>

        {selectedSong && (
          <SongDetailsModal
            song={selectedSong}
            isFavorite={favorites.some(fav => fav.id === selectedSong.id)}
            isInQueue={currentEvent ? (queues[currentEvent.eventId]?.some(q => q.songId === selectedSong.id) || false) : false}
            onClose={() => setSelectedSong(null)}
            onToggleFavorite={toggleFavorite}
            onAddToQueue={addToEventQueue}
            eventId={currentEvent?.eventId}
            checkedIn={checkedIn}
            isCurrentEventLive={isCurrentEventLive}
          />
        )}
      </div>
    );
  } catch (error) {
    console.error('[EXPLORE_SONGS] Render error:', error);
    return <div>Error in ExploreSongs: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default ExploreSongs;