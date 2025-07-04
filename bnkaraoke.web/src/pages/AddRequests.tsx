import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import toast, { Toaster } from 'react-hot-toast';
import './AddRequests.css';
import { API_ROUTES } from '../config/apiConfig';
import { SpotifySong, Song } from '../types';
import Modals from '../components/Modals';

const API_BASE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com' : 'http://localhost:7290';

interface Requestor {
  userName: string;
  fullName: string;
}

interface AddedSong {
  title: string;
  artist: string;
  requestor: string;
}

interface User {
  userName?: string;
  username?: string;
  firstName?: string;
  first_name?: string;
  lastName?: string;
  last_name?: string;
}

const AddRequests: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [spotifySongs, setSpotifySongs] = useState<SpotifySong[]>([]);
  const [selectedSpotifySong, setSelectedSpotifySong] = useState<SpotifySong | null>(null);
  const [showSpotifyModal, setShowSpotifyModal] = useState<boolean>(false);
  const [showSpotifyDetailsModal, setShowSpotifyDetailsModal] = useState<boolean>(false);
  const [showRequestorModal, setShowRequestorModal] = useState<boolean>(false);
  const [showConfirmationModal, setShowConfirmationModal] = useState<boolean>(false);
  const [requestedSong, setRequestedSong] = useState<SpotifySong | null>(null);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [isSearching, setIsSearching] = useState<boolean>(false);
  const [requestors, setRequestors] = useState<Requestor[]>([]);
  const [selectedRequestor, setSelectedRequestor] = useState<string>("");
  const [addedSongs, setAddedSongs] = useState<AddedSong[]>([]);
  const [requestorFetchError, setRequestorFetchError] = useState<string | null>(null);
  const [roles, setRoles] = useState<string[]>(JSON.parse(localStorage.getItem("roles") || "[]"));
  const searchInputRef = useRef<HTMLInputElement>(null);
  const requestorSelectRef = useRef<HTMLSelectElement>(null);

  // Log localStorage on mount
  useEffect(() => {
    console.log("[ADD_REQUESTS] Initializing with localStorage:", {
      roles: localStorage.getItem("roles"),
      userName: localStorage.getItem("userName"),
      token: localStorage.getItem("token")?.slice(0, 10) + "..."
    });
  }, []);

  // Sync roles state with localStorage changes
  useEffect(() => {
    const maxRetries = 5;
    let retryCount = 0;

    const syncRoles = () => {
      const newRoles = JSON.parse(localStorage.getItem("roles") || "[]") as string[];
      console.log("[ADD_REQUESTS] localStorage roles updated:", newRoles);
      setRoles(newRoles);
      if (newRoles.length === 0 && retryCount < maxRetries) {
        retryCount++;
        console.log(`[ADD_REQUESTS] Retrying roles sync (attempt ${retryCount}/${maxRetries})`);
        setTimeout(syncRoles, 3000);
      }
    };

    window.addEventListener("storage", syncRoles);
    syncRoles(); // Initial check
    return () => window.removeEventListener("storage", syncRoles);
  }, []);

  // Log requestors changes to ensure re-render
  useEffect(() => {
    console.log("[ADD_REQUESTS] Requestors updated:", requestors);
  }, [requestors]);

  // Focus requestor select when modal opens
  useEffect(() => {
    console.log("[ADD_REQUESTS] showRequestorModal changed:", showRequestorModal);
    if (showRequestorModal && requestorSelectRef.current) {
      requestorSelectRef.current.focus();
    }
  }, [showRequestorModal]);

  // Check Application Manager, Karaoke DJ, or Song Manager role
  useEffect(() => {
    console.log("[ADD_REQUESTS] Checking roles:", roles);
    if (!roles.includes("Application Manager") && !roles.includes("Karaoke DJ") && !roles.includes("Song Manager")) {
      console.error("[ADD_REQUESTS] Unauthorized access: Application Manager, Karaoke DJ, or Song Manager required");
      toast.error("Unauthorized access. Please log in as an application manager, karaoke DJ, or song manager.");
      navigate("/login");
    }
  }, [navigate, roles]);

  // Token validation
  const validateToken = useCallback(async (): Promise<string | null> => {
    const token = localStorage.getItem("token");
    const isLoginPage = ["/", "/login", "/register", "/change-password"].includes(location.pathname);
    if (!token) {
      console.error("[ADD_REQUESTS] No token found");
      if (!isLoginPage) {
        setSearchError("Authentication token missing. Please log in again.");
        navigate("/login");
      }
      return null;
    }

    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[ADD_REQUESTS] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        if (!isLoginPage) {
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          setSearchError("Session expired. Please log in again.");
          navigate("/login");
        }
        return null;
      }
      console.log("[ADD_REQUESTS] Token validated");
      return token;
    } catch (err) {
      console.error("[ADD_REQUESTS] Token validation error:", err);
      if (!isLoginPage) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setSearchError("Invalid token. Please log in again.");
        navigate("//login");
      }
      return null;
    }
  }, [navigate, location.pathname]);

  // Fetch and cache requestors
  const fetchRequestors = useCallback(async (retryCount = 0, maxRetries = 3) => {
    console.log("[ADD_REQUESTS] fetchRequestors started, attempt:", retryCount + 1);
    const token = await validateToken();
    if (!token) {
      console.log("[ADD_REQUESTS] fetchRequestors skipped: no valid token");
      return;
    }

    console.log("[ADD_REQUESTS] fetchRequestors executing with token:", token.slice(0, 10), "...");
    const roles = JSON.parse(localStorage.getItem("roles") || "[]") as string[];
    console.log("[ADD_REQUESTS] fetchRequestors roles check:", roles);
    const hasAccess = roles.some(role => ["Karaoke DJ", "User Manager", "Song Manager", "Application Manager"].includes(role));
    if (!hasAccess) {
      console.error("[ADD_REQUESTS] User lacks required roles for /api/auth/users:", roles);
      setRequestorFetchError("You do not have permission to view the requestors list. Required roles: Karaoke DJ, User Manager, Song Manager, or Application Manager.");
      console.log("[ADD_REQUESTS] fetchRequestors exited: no access");
      return;
    }

    try {
      console.log("[ADD_REQUESTS] Fetching requestors from: /api/auth/users");
      const response = await fetch(`${API_BASE_URL}/api/auth/users`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[ADD_REQUESTS] Fetch requestors failed: ${response.status} - ${errorText}`);
        if (retryCount < maxRetries && response.status !== 401) {
          console.log(`[ADD_REQUESTS] Retrying fetchRequestors (attempt ${retryCount + 2}/${maxRetries + 1})`);
          setTimeout(() => fetchRequestors(retryCount + 1, maxRetries), 2000);
          return;
        }
        setRequestorFetchError(`Failed to load requestors: ${errorText || response.statusText}`);
        throw new Error(`Fetch requestors failed: ${response.status}`);
      }
      const data = await response.json();
      console.log("[ADD_REQUESTS] Fetched users raw:", JSON.stringify(data, null, 2));
      if (!Array.isArray(data)) {
        console.error("[ADD_REQUESTS] Fetched users data is not an array:", data);
        setRequestorFetchError("Invalid user data format. Please contact support.");
        return;
      }
      const requestorList = data
        .filter((user: User) => {
          const userName = user.userName || user.username;
          const firstName = user.firstName || user.first_name;
          const lastName = user.lastName || user.last_name;
          if (!userName || !firstName || !lastName) {
            console.warn("[ADD_REQUESTS] Invalid user data:", user);
            return false;
          }
          return true;
        })
        .map((user: User) => ({
          userName: user.userName || user.username,
          fullName: `${user.firstName || user.first_name} ${user.lastName || user.last_name}`,
        }))
        .sort((a: Requestor, b: Requestor) => {
          const nameA = a.fullName.toLowerCase();
          const nameB = b.fullName.toLowerCase();
          return nameA.localeCompare(nameB);
        });
      console.log("[ADD_REQUESTS] Sorted requestors:", requestorList);
      if (requestorList.length === 0) {
        console.warn("[ADD_REQUESTS] No valid requestors found in response");
        setRequestorFetchError("No valid users found. Please try again or contact support.");
      } else {
        setRequestors(requestorList);
        setRequestorFetchError(null);
      }
    } catch (err) {
      console.error("[ADD_REQUESTS] Fetch requestors error:", err);
      setRequestorFetchError("Failed to load requestors. Please try again or contact support.");
    }
    console.log("[ADD_REQUESTS] fetchRequestors completed");
  }, [validateToken]);

  // Debug: Set roles and fetch users for testing
  const setDebugRoles = async () => {
    const debugRoles = ["Karaoke DJ", "Song Manager"];
    const currentToken = localStorage.getItem("token");
    const currentUserName = localStorage.getItem("userName");
    if (!currentToken || !currentUserName) {
      toast.error("No valid token or username found. Please log in first.");
      console.error("[ADD_REQUESTS] Debug: Missing token or username", { token: currentToken, userName: currentUserName });
      navigate("/login");
      return;
    }
    localStorage.setItem("roles", JSON.stringify(debugRoles));
    setRoles(debugRoles);
    console.log("[ADD_REQUESTS] Debug roles set:", debugRoles);
    console.log("[ADD_REQUESTS] localStorage after debug:", {
      roles: localStorage.getItem("roles"),
      userName: localStorage.getItem("userName"),
      token: localStorage.getItem("token")?.slice(0, 10) + "..."
    });

    // Fetch users for debugging
    try {
      console.log("[ADD_REQUESTS] Debug: Fetching users from /api/auth/users");
      const response = await fetch(`${API_BASE_URL}/api/auth/users`, {
        headers: { Authorization: `Bearer ${currentToken}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[ADD_REQUESTS] Debug: Fetch users failed: ${response.status} - ${errorText}`);
        toast.error(`Debug: Failed to fetch users: ${errorText || response.statusText}`);
        return;
      }
      const data = await response.json();
      console.log("[ADD_REQUESTS] Debug: Fetched users:", JSON.stringify(data, null, 2));
      const requestorList = data
        .filter((user: User) => {
          const userName = user.userName || user.username;
          const firstName = user.firstName || user.first_name;
          const lastName = user.lastName || user.last_name;
          if (!userName || !firstName || !lastName) {
            console.warn("[ADD_REQUESTS] Debug: Invalid user data:", user);
            return false;
          }
          return true;
        })
        .map((user: User) => ({
          userName: user.userName || user.username,
          fullName: `${user.firstName || user.first_name} ${user.lastName || user.last_name}`,
        }))
        .sort((a: Requestor, b: Requestor) => a.fullName.toLowerCase().localeCompare(b.fullName.toLowerCase()));
      console.log("[ADD_REQUESTS] Debug: Sorted requestors:", requestorList);
      setRequestors(requestorList);
      setRequestorFetchError(null);
      // Display user list in toast
      const userListText = requestorList.map((s: Requestor) => s.fullName).join(", ") || "No valid users found";
      toast.success(`Debug: Roles set and users fetched: ${userListText}`, { duration: 5000 });
    } catch (err) {
      console.error("[ADD_REQUESTS] Debug: Fetch users error:", err);
      toast.error("Debug: Failed to fetch users. Please try again.");
    }
  };

  // Fetch requestors on mount with delay
  useEffect(() => {
    console.log("[ADD_REQUESTS] Scheduling fetchRequestors");
    const timer = setTimeout(() => fetchRequestors(), 1000);
    return () => clearTimeout(timer);
  }, [fetchRequestors]);

  // Fetch Spotify songs
  const fetchSpotifySongs = useCallback(async () => {
    if (!searchQuery.trim()) {
      console.log("[ADD_REQUESTS] Search query is empty, resetting");
      setSpotifySongs([]);
      setSearchError(null);
      return;
    }
    const token = await validateToken();
    if (!token) return;

    setIsSearching(true);
    setSearchError(null);
    console.log(`[ADD_REQUESTS] Fetching Spotify songs with query: ${searchQuery}`);
    try {
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        const errorText = await response.text();
        console.error(`[ADD_REQUESTS] Spotify fetch failed: ${response.status} - ${errorText}`);
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
      console.log("[ADD_REQUESTS] Spotify fetch response:", data);
      const fetchedSpotifySongs = (data.songs as SpotifySong[]) || [];
      console.log("[ADD_REQUESTS] Fetched Spotify songs:", fetchedSpotifySongs);
      setSpotifySongs(fetchedSpotifySongs);
      setShowSpotifyModal(true);
    } catch (err) {
      console.error("[ADD_REQUESTS] Spotify search error:", err);
      setSearchError("An error occurred while searching Spotify. Please try again.");
      setShowSpotifyModal(true);
    } finally {
      setIsSearching(false);
    }
  }, [searchQuery, validateToken, navigate]);

  // Handle Spotify song selection
  const handleSpotifySongSelect = useCallback((song: SpotifySong) => {
    console.log("[ADD_REQUESTS] Selected Spotify song:", JSON.stringify(song, null, 2));
    setSelectedSpotifySong(song);
    setShowSpotifyModal(false);
    setShowSpotifyDetailsModal(true);
  }, []);

  // Confirm song request
  const confirmSongRequest = useCallback(() => {
    if (!selectedSpotifySong || !selectedRequestor) {
      console.error("[ADD_REQUESTS] Missing selected song or requestor in confirmation");
      setSearchError("Please select a song and a requestor.");
      return;
    }
    console.log("[ADD_REQUESTS] Confirming song request:", {
      title: selectedSpotifySong.title,
      artist: selectedSpotifySong.artist,
      requestor: requestors.find(s => s.userName === selectedRequestor)?.fullName,
      requestorsLength: requestors.length,
      showRequestorModal
    });
    setShowRequestorModal(false);
    setShowConfirmationModal(true);
  }, [selectedSpotifySong, selectedRequestor, requestors, showRequestorModal]);

  // Submit song request on behalf of requestor
  const submitSongRequest = useCallback(async () => {
    if (!selectedSpotifySong || !selectedRequestor) {
      console.error("[ADD_REQUESTS] Missing selected song or requestor");
      setSearchError("Please select a song and a requestor.");
      return;
    }

    const token = await validateToken();
    if (!token) return;

    const requestor = requestors.find(s => s.userName === selectedRequestor);
    if (!requestor) {
      console.error("[ADD_REQUESTS] Selected requestor not found:", selectedRequestor);
      setSearchError("Selected requestor not found.");
      return;
    }

    const requestData = {
      title: selectedSpotifySong.title || "Unknown Title",
      artist: selectedSpotifySong.artist || "Unknown Artist",
      spotifyId: selectedSpotifySong.id,
      bpm: selectedSpotifySong.bpm || 0,
      danceability: selectedSpotifySong.danceability || 0,
      energy: selectedSpotifySong.energy || 0,
      valence: selectedSpotifySong.valence || null,
      popularity: selectedSpotifySong.popularity || 0,
      genre: selectedSpotifySong.genre || null,
      decade: selectedSpotifySong.decade || null,
      status: "pending",
      requestedBy: selectedRequestor,
    };

    console.log("[ADD_REQUESTS] Sending song request payload:", requestData);

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
      console.log(`[ADD_REQUESTS] Song request response status: ${response.status}, body: ${responseText}`);

      if (!response.ok) {
        console.error(`[ADD_REQUESTS] Failed to submit song request: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setSearchError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        throw new Error(`Song request failed: ${response.status} - ${responseText}`);
      }

      console.log("[ADD_REQUESTS] Song request successful");
      setAddedSongs(prev => [
        ...prev,
        {
          title: selectedSpotifySong.title,
          artist: selectedSpotifySong.artist,
          requestor: requestor.fullName,
        },
      ]);
      setRequestedSong(selectedSpotifySong);
      setShowConfirmationModal(false);
      toast.success(`Song requested for ${requestor.fullName}`);
      resetSearch();
    } catch (err) {
      console.error("[ADD_REQUESTS] Song request error:", err);
      setSearchError("Failed to submit song request. Please try again.");
    } finally {
      setIsSearching(false);
    }
  }, [selectedSpotifySong, selectedRequestor, requestors, validateToken, navigate]);

  // Reset search state
  const resetSearch = useCallback(() => {
    console.log("[ADD_REQUESTS] resetSearch called");
    setSearchQuery("");
    setSpotifySongs([]);
    setSelectedSpotifySong(null);
    setShowSpotifyModal(false);
    setShowSpotifyDetailsModal(false);
    setShowRequestorModal(false);
    setShowConfirmationModal(false);
    setRequestedSong(null);
    setSelectedRequestor("");
    setSearchError(null);
  }, []);

  // Search bar handlers
  const handleSearchClick = useCallback(() => {
    console.log("[ADD_REQUESTS] Search button clicked");
    fetchSpotifySongs();
  }, [fetchSpotifySongs]);

  const handleSearchKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      console.log("[ADD_REQUESTS] Enter key pressed in search");
      fetchSpotifySongs();
    }
  }, [fetchSpotifySongs]);

  const handleSearchChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchQuery(e.target.value);
  }, []);

  return (
    <div className="add-requests">
      <Toaster />
      <div className="add-requests-content">
        {searchError && <p className="error-text">{searchError}</p>}
        {requestorFetchError && <p className="error-text">{requestorFetchError}</p>}
        <h2>Add Song Requests</h2>
        {process.env.NODE_ENV === 'development' && (
          <button onClick={setDebugRoles} style={{ marginBottom: '10px' }}>
            Debug: Set Test Roles & Show Users
          </button>
        )}
        <div className="search-section">
          <div className="search-bar-container">
            <input
              ref={searchInputRef}
              type="text"
              placeholder="Search for Karaoke Songs to Sing"
              value={searchQuery}
              onChange={handleSearchChange}
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
              <svg width="16" height="16" viewBox="0 0 16 16" fill="white">
                <path d="M11.742 10.344a6.5 6.5 0 1 0-1.397 1.398h-.001c.03.04.062.078.098.115l3.85 3.85a1 1 0 0 0 1.415-1.414l-3.85-3.85a1.007 1.007 0 0 0-.115-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0z"/>
              </svg>
            </button>
            <button
              onClick={resetSearch}
              onTouchStart={resetSearch}
              className="reset-button"
              aria-label="Reset search"
            >
              <svg width="16" height="16" viewBox="0 0 16 16" fill="white">
                <path d="M8 0a8 8 0 1 0 0 16A8 8 0 0 0 8 0zm3.5 11.5a.5.5 0 0 1-.707 0L8 8.707 5.207 11.5a.5.5 0 0 1-.707-.707L7.293 8 4.5 5.207a.5.5 0 0 1 .707-.707L8 7.293 10.793 4.5a.5.5 0 0 1 .707.707L8.707 8l2.793 2.793a.5.5 0 0 1 0 .707z"/>
              </svg>
            </button>
          </div>
        </div>
        <div className="added-songs-panel">
          <h3>Added Songs This Session</h3>
          {addedSongs.length === 0 ? (
            <p>No songs added yet.</p>
          ) : (
            <div className="added-songs-list">
              {addedSongs.map((song, index) => (
                <div key={index} className="added-song">
                  <span>{song.title} - {song.artist} (Requested for: {song.requestor})</span>
                </div>
              ))}
            </div>
          )}
        </div>
        <Modals
          isSearching={isSearching}
          searchError={searchError}
          songs={[]}
          spotifySongs={spotifySongs}
          selectedSpotifySong={selectedSpotifySong}
          requestedSong={requestedSong}
          selectedSong={null}
          showSearchModal={false}
          showSpotifyModal={showSpotifyModal}
          showSpotifyDetailsModal={showSpotifyDetailsModal}
          showRequestConfirmationModal={false}
          showReorderErrorModal={false}
          reorderError={null}
          fetchSpotifySongs={fetchSpotifySongs}
          handleSpotifySongSelect={handleSpotifySongSelect}
          submitSongRequest={() => {
            console.log("[ADD_REQUESTS] submitSongRequest triggered, requestors:", requestors.length);
            setShowRequestorModal(true);
          }}
          resetSearch={resetSearch}
          setShowSpotifyDetailsModal={setShowSpotifyDetailsModal}
          setSearchError={setSearchError}
          setSelectedSong={async (song: Song) => console.log("[ADD_REQUESTS] setSelectedSong called:", song)}
          setShowReorderErrorModal={() => console.log("[ADD_REQUESTS] setShowReorderErrorModal called")}
          setSelectedQueueId={(queueId: number) => console.log("[ADD_REQUESTS] setSelectedQueueId called:", queueId)}
          favorites={[]}
          myQueues={{}}
          isSingerOnly={false}
          toggleFavorite={async (song: Song) => console.log("[ADD_REQUESTS] toggleFavorite called:", song)}
          addToEventQueue={async (song: Song, eventId: number) => console.log("[ADD_REQUESTS] addToEventQueue called:", song, eventId)}
          handleDeleteSong={async (eventId: number, queueId: number) => console.log("[ADD_REQUESTS] handleDeleteSong called:", eventId, queueId)}
          currentEvent={null}
          checkedIn={false}
          isCurrentEventLive={false}
          selectedQueueId={undefined}
        />
        {showRequestorModal && (
          <div className="modal">
            <div className="modal-content">
              <h3>Select Requestor</h3>
              {requestors.length === 0 ? (
                <p className="error-text">No requestors available. Please try again or contact support.</p>
              ) : (
                <select
                  ref={requestorSelectRef}
                  value={selectedRequestor}
                  onChange={(e) => setSelectedRequestor(e.target.value)}
                >
                  <option value="">Select a requestor</option>
                  {requestors.map(requestor => (
                    <option key={requestor.userName} value={requestor.userName}>
                      {requestor.fullName}
                    </option>
                  ))}
                </select>
              )}
              <button
                onClick={confirmSongRequest}
                disabled={!selectedRequestor || isSearching || requestors.length === 0}
              >
                {isSearching ? "Submitting..." : "Confirm Requestor"}
              </button>
              <button onClick={() => setShowRequestorModal(false)} disabled={isSearching} className="cancel">
                Cancel
              </button>
            </div>
          </div>
        )}
        {showConfirmationModal && (
          <div className="modal">
            <div className="modal-content">
              <h3>Confirm Song Request</h3>
              <p>
                Request "{selectedSpotifySong?.title || 'Unknown Title'}" by {selectedSpotifySong?.artist || 'Unknown Artist'} for {requestors.find(s => s.userName === selectedRequestor)?.fullName || 'Unknown User'}?
              </p>
              <button onClick={submitSongRequest} disabled={isSearching || !selectedSpotifySong || !selectedRequestor}>
                {isSearching ? "Submitting..." : "Confirm"}
              </button>
              <button onClick={() => setShowConfirmationModal(false)} disabled={isSearching} className="cancel">
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default AddRequests;