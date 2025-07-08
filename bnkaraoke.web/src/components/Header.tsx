// src/components/Header.tsx
import React, { useState, useEffect, useRef, useCallback, memo } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { LogoutOutlined } from '@ant-design/icons';
import toast from 'react-hot-toast';
import "./Header.css";
import { API_ROUTES } from "../config/apiConfig";
import { useEventContext } from "../context/EventContext";
import { AttendanceAction, Event } from "../types";

const Header: React.FC = memo(() => {
  console.log("[HEADER] Rendering");

  const navigate = useNavigate();
  const location = useLocation();
  const { currentEvent, setCurrentEvent, checkedIn, setCheckedIn, isCurrentEventLive, setIsCurrentEventLive, isOnBreak, setIsOnBreak, liveEvents, setLiveEvents, upcomingEvents, setUpcomingEvents } = useEventContext();
  const [firstName, setFirstName] = useState(localStorage.getItem("firstName") || "");
  const [lastName, setLastName] = useState(localStorage.getItem("lastName") || "");
  const [roles, setRoles] = useState<string[]>(JSON.parse(localStorage.getItem("roles") || "[]"));
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [isEventDropdownOpen, setIsEventDropdownOpen] = useState(false);
  const [isPreselectDropdownOpen, setIsPreselectDropdownOpen] = useState(false);
  const [isCheckingIn, setIsCheckingIn] = useState(false);
  const [checkInError, setCheckInError] = useState<string | null>(null);
  const [isLoadingEvents, setIsLoadingEvents] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [showLeaveConfirmation, setShowLeaveConfirmation] = useState(false);
  const eventDropdownRef = useRef<HTMLDivElement>(null);
  const preselectDropdownRef = useRef<HTMLDivElement>(null);
  const userName = localStorage.getItem("userName") || "";
  const fetchEventsTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  const adminRoles = ["Application Manager", "Karaoke DJ", "Song Manager", "User Manager", "Queue Manager", "Event Manager"];
  const hasAdminRole = roles.some(role => adminRoles.includes(role));

  const validateToken = useCallback(() => {
    const token = localStorage.getItem("token");
    if (!token || !userName) {
      console.error("[VALIDATE_TOKEN] No token or userName found");
      setFetchError("Authentication token or username missing. Please log in again.");
      if (!["/", "/login", "/register", "/change-password"].includes(location.pathname)) {
        navigate("/login");
      }
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[VALIDATE_TOKEN] Malformed token: does not contain three parts");
        setFetchError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[VALIDATE_TOKEN] Token expired:", new Date(exp).toISOString());
        setFetchError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[VALIDATE_TOKEN] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[VALIDATE_TOKEN] Error:", err);
      setFetchError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  }, [navigate, userName, location.pathname]);

  const token = localStorage.getItem("token");
  if (!token || !userName) {
    console.log("[HEADER] Skipped rendering: no token or userName");
    return null;
  }

  useEffect(() => {
    const syncLocalStorage = () => {
      const newFirstName = localStorage.getItem("firstName") || "";
      const newLastName = localStorage.getItem("lastName") || "";
      const storedRoles = localStorage.getItem("roles");
      if (newFirstName !== firstName) setFirstName(newFirstName);
      if (newLastName !== lastName) setLastName(newLastName);
      if (storedRoles) {
        try {
          const parsedRoles = JSON.parse(storedRoles) || [];
          if (JSON.stringify(parsedRoles) !== JSON.stringify(roles)) {
            setRoles(parsedRoles);
            console.log("[SYNC_LOCAL_STORAGE] Updated roles from localStorage:", parsedRoles);
          }
        } catch (parseErr) {
          console.error("[SYNC_LOCAL_STORAGE] Parse roles error:", parseErr);
        }
      }
    };

    window.addEventListener("storage", syncLocalStorage);
    syncLocalStorage();
    return () => window.removeEventListener("storage", syncLocalStorage);
  }, [firstName, lastName, roles]);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      try {
        if (eventDropdownRef.current && !eventDropdownRef.current.contains(event.target as Node)) {
          setIsEventDropdownOpen(false);
        }
        if (preselectDropdownRef.current && !preselectDropdownRef.current.contains(event.target as Node)) {
          setIsPreselectDropdownOpen(false);
        }
      } catch (err) {
        console.error("[HANDLE_CLICK_OUTSIDE] Error:", err);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const checkAttendanceStatus = useCallback(async (event: Event) => {
    const token = validateToken();
    if (!token) return false;

    try {
      console.log(`[CHECK_ATTENDANCE] Status for event: ${event.eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[CHECK_ATTENDANCE] Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          navigate("/login");
          return false;
        }
        throw new Error(`Failed to fetch attendance status: ${response.status} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      console.log(`[CHECK_ATTENDANCE] Status for event ${event.eventId}:`, data);
      return data.isCheckedIn || false;
    } catch (err) {
      console.error("[CHECK_ATTENDANCE] Error:", err);
      setCheckInError(err instanceof Error ? err.message : "Failed to check attendance status");
      toast.error("Failed to check attendance status. Please try again.");
      return false;
    }
  }, [validateToken, navigate]);

  const handleCheckIn = useCallback(async (event: Event) => {
    const token = validateToken();
    if (!token) return;

    setIsCheckingIn(true);
    setCheckInError(null);

    try {
      const isAlreadyCheckedIn = await checkAttendanceStatus(event);
      if (isAlreadyCheckedIn) {
        console.log(`[CHECK_IN] User already checked in for event ${event.eventId}`);
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
        setIsEventDropdownOpen(false);
        navigate("/dashboard");
        toast.success(`Already checked into event ${event.description}!`);
        return;
      }

      const requestData: AttendanceAction = { RequestorId: userName };
      console.log(`[CHECK_IN] Attempting check-in for event: ${event.eventId}, payload:`, JSON.stringify(requestData));
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}/attendance/check-in`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log("[CHECK_IN] Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        console.error(`[CHECK_IN] Failed for event ${event.eventId}: ${response.status} - ${responseText}`);
        let errorMessage = "Check-in failed. Please try again.";
        if (response.status === 401) {
          errorMessage = "Session expired. Please log in again.";
          navigate("/login");
        } else if (response.status === 400) {
          errorMessage = responseText ? JSON.parse(responseText).message : "Invalid request.";
        } else if (response.status === 404) {
          errorMessage = "Event not found.";
        }
        setCheckInError(errorMessage);
        toast.error(errorMessage);
        return;
      }

      console.log(`[CHECK_IN] Success for event ${event.eventId}`);
      setCurrentEvent(event);
      setCheckedIn(true);
      setIsCurrentEventLive(event.status.toLowerCase() === "live");
      setIsOnBreak(false);
      setIsEventDropdownOpen(false);
      navigate("/dashboard");
      toast.success(`Checked into event ${event.description} successfully!`);
    } catch (err) {
      console.error("[CHECK_IN] Error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to check in.";
      setCheckInError(errorMessage);
      toast.error(errorMessage);
    } finally {
      setIsCheckingIn(false);
    }
  }, [validateToken, checkAttendanceStatus, navigate, setCurrentEvent, setCheckedIn, setIsCurrentEventLive, setIsOnBreak]);

  const debounceFetchEvents = useCallback((callback: () => Promise<void>) => {
    if (fetchEventsTimeoutRef.current) {
      clearTimeout(fetchEventsTimeoutRef.current);
    }
    fetchEventsTimeoutRef.current = setTimeout(callback, 1000);
  }, []);

  const fetchEvents = useCallback(async () => {
    const token = validateToken();
    if (!token) return;

    try {
      setIsLoadingEvents(true);
      setFetchError(null);
      console.log(`[FETCH_EVENTS] Fetching from: ${API_ROUTES.EVENTS}`);
      const response = await fetch(API_ROUTES.EVENTS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[FETCH_EVENTS] Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        const errorMessage = response.status === 401
          ? "Session expired. Please log in again."
          : response.status === 403
          ? "Unable to fetch events due to authorization error. Please contact support."
          : `Fetch events failed: ${response.status} - ${responseText}`;
        throw new Error(errorMessage);
      }
      let eventsData: Event[];
      try {
        eventsData = JSON.parse(responseText);
      } catch (jsonError) {
        console.error("[FETCH_EVENTS] JSON parse error:", jsonError, "Raw response:", responseText);
        throw new Error("Invalid events response format");
      }
      console.log("[FETCH_EVENTS] Fetched events:", eventsData);

      const live = eventsData.filter(e =>
        e.status.toLowerCase() === "live" &&
        e.visibility.toLowerCase() === "visible" &&
        !e.isCanceled
      ) || [];
      const upcoming = eventsData.filter(e =>
        e.status.toLowerCase() === "upcoming" &&
        e.visibility.toLowerCase() === "visible" &&
        !e.isCanceled
      ) || [];
      setLiveEvents(live);
      setUpcomingEvents(upcoming);
      console.log("[FETCH_EVENTS] Live events:", live);
      console.log("[FETCH_EVENTS] Upcoming events:", upcoming);
    } catch (err) {
      console.error("[FETCH_EVENTS] Error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to load events";
      setFetchError(errorMessage);
      setLiveEvents([]);
      setUpcomingEvents([]);
      toast.error(errorMessage);
      if (errorMessage.includes("Session expired")) {
        navigate("/login");
      }
    } finally {
      setIsLoadingEvents(false);
    }
  }, [validateToken, navigate, setLiveEvents, setUpcomingEvents]);

  useEffect(() => {
    debounceFetchEvents(fetchEvents);
    return () => {
      if (fetchEventsTimeoutRef.current) {
        clearTimeout(fetchEventsTimeoutRef.current);
      }
    };
  }, [debounceFetchEvents, fetchEvents]);

  useEffect(() => {
    console.log("[HEADER_STATE] Update:", {
      checkedIn,
      isCurrentEventLive,
      currentEvent: currentEvent ? { eventId: currentEvent.eventId, description: currentEvent.description } : null,
      isOnBreak,
      isLoadingEvents,
      liveEvents: liveEvents.map(e => ({ eventId: e.eventId, description: e.description })),
      upcomingEvents: upcomingEvents.map(e => ({ eventId: e.eventId, description: e.description })),
      isEventDropdownOpen,
      roles,
      hasAdminRole
    });
  }, [checkedIn, isCurrentEventLive, currentEvent, isOnBreak, isLoadingEvents, liveEvents, upcomingEvents, isEventDropdownOpen, roles, hasAdminRole]);

  const handleNavigation = useCallback((path: string) => {
    try {
      setIsDropdownOpen(false);
      navigate(path);
    } catch (err) {
      console.error("[HANDLE_NAVIGATION] Error:", err);
    }
  }, [navigate]);

  const handleLogout = useCallback(async () => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[LOGOUT] Sending request to: /api/auth/logout`);
      const response = await fetch('/api/auth/logout', {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
      });
      const responseText = await response.text();
      console.log("[LOGOUT] Response:", { status: response.status, body: responseText });

      if (!response.ok) {
        console.error("[LOGOUT] Failed:", response.status, responseText);
        toast.error(response.status === 401 ? "Session expired. Please log in again." : "Failed to log out. Please try again.");
        if (response.status === 401) {
          navigate("/login");
        }
        return;
      }

      // Preserve auth data
      const authData = {
        token: localStorage.getItem("token"),
        userName: localStorage.getItem("userName"),
        firstName: localStorage.getItem("firstName"),
        lastName: localStorage.getItem("lastName"),
        roles: localStorage.getItem("roles"),
      };
      localStorage.clear();
      Object.entries(authData).forEach(([key, value]) => {
        if (value) localStorage.setItem(key, value);
      });

      setTimeout(() => {
        navigate("/login");
      }, 0);
      toast.success("Logged out successfully!");
    } catch (err) {
      console.error("[LOGOUT] Error:", err);
      toast.error("Failed to log out. Please try again.");
    }
  }, [validateToken, navigate]);

  const handlePreselectSongs = useCallback((event: Event) => {
    if (event.status.toLowerCase() === "live") {
      console.log("[PRESELECT] Skipping preselect for live event:", event.eventId);
      handleCheckIn(event);
      return;
    }
    try {
      console.log("[PRESELECT] Event:", event.eventId);
      setCurrentEvent(event);
      setIsCurrentEventLive(false);
      setCheckedIn(false);
      setIsOnBreak(false);
      setIsPreselectDropdownOpen(false);
      navigate("/dashboard");
    } catch (err) {
      console.error("[PRESELECT] Error:", err);
      toast.error("Failed to preselect event. Please try again.");
    }
  }, [navigate, setCurrentEvent, setIsCurrentEventLive, setCheckedIn, setIsOnBreak, handleCheckIn]);

  const handleLeaveEvent = useCallback(async () => {
    if (!currentEvent) {
      console.error("[LEAVE_EVENT] No current event selected");
      toast.error("No event selected to leave.");
      return;
    }
    setShowLeaveConfirmation(true);
  }, [currentEvent]);

  const confirmLeaveEvent = useCallback(async () => {
    const token = validateToken();
    if (!token || !currentEvent) {
      console.error("[LEAVE_EVENT] Missing token or current event", { token: !!token, currentEvent });
      toast.error("Cannot leave event: missing authentication or event.");
      setShowLeaveConfirmation(false);
      return;
    }

    try {
      const requestData: AttendanceAction = { RequestorId: userName };
      console.log(`[LEAVE_EVENT] Leaving event: ${currentEvent.eventId}, payload:`, JSON.stringify(requestData));
      const endpoint = `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/check-out`;
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log("[LEAVE_EVENT] Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        console.error(`[LEAVE_EVENT] Failed for event ${currentEvent.eventId}: ${response.status} - ${responseText}`);
        let errorMessage = "Failed to leave event.";
        if (response.status === 401) {
          errorMessage = "Session expired. Please log in again.";
          navigate("/login");
        } else if (response.status === 404) {
          errorMessage = "Event not found.";
        } else if (response.status === 400) {
          errorMessage = responseText ? JSON.parse(responseText).message : "Invalid request.";
        }
        toast.error(errorMessage);
        return;
      }

      console.log(`[LEAVE_EVENT] Success: ${currentEvent.eventId}`);
      localStorage.removeItem("recentlyLeftEvent");
      localStorage.removeItem("recentlyLeftEventTimestamp");
      localStorage.removeItem("currentEvent");
      localStorage.removeItem("checkedIn");
      localStorage.removeItem("isCurrentEventLive");
      localStorage.removeItem("isOnBreak");
      localStorage.removeItem("liveEvents");
      localStorage.removeItem("upcomingEvents");
      setCurrentEvent(null);
      setCheckedIn(false);
      setIsCurrentEventLive(false);
      setIsOnBreak(false);
      setShowLeaveConfirmation(false);
      navigate("/dashboard");
      toast.success("Left event successfully!");
      await fetchEvents();
    } catch (err) {
      console.error("[LEAVE_EVENT] Error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to leave the event.";
      toast.error(errorMessage);
    } finally {
      setShowLeaveConfirmation(false);
    }
  }, [validateToken, currentEvent, navigate, setCurrentEvent, setCheckedIn, setIsCurrentEventLive, setIsOnBreak, fetchEvents]);

  const cancelLeaveEvent = useCallback(() => {
    setShowLeaveConfirmation(false);
  }, []);

  const handleBreakToggle = useCallback(async () => {
    const token = validateToken();
    if (!token || !currentEvent) {
      console.error("[BREAK_TOGGLE] Missing token or current event", { token: !!token, currentEvent });
      toast.error("Cannot toggle break: missing authentication or event.");
      return;
    }

    try {
      const requestData: AttendanceAction = { RequestorId: userName };
      const endpoint = isOnBreak
        ? `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/break/end`
        : `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/break/start`;
      console.log(`[BREAK_TOGGLE] Endpoint: ${endpoint}, payload:`, JSON.stringify(requestData));
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log("[BREAK_TOGGLE] Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        let errorMessage = "Failed to toggle break status.";
        if (response.status === 400) {
          errorMessage = responseText ? JSON.parse(responseText).message : "Invalid request.";
        } else if (response.status === 401) {
          errorMessage = "Session expired. Please log in again.";
          navigate("/login");
        } else if (response.status === 404) {
          errorMessage = "Event not found.";
        }
        console.error(`[BREAK_TOGGLE] Failed for event ${currentEvent.eventId}: ${response.status} - ${responseText}`);
        toast.error(errorMessage);
        return;
      }
      console.log(`[BREAK_TOGGLE] Success: ${isOnBreak ? 'Returned from break' : 'On break'}`);
      setIsOnBreak(!isOnBreak);
      toast.success(isOnBreak ? "Returned from break successfully!" : "Break started successfully!");
    } catch (err) {
      console.error("[BREAK_TOGGLE] Error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to toggle break status.";
      toast.error(errorMessage);
    }
  }, [validateToken, currentEvent, isOnBreak, navigate, setIsOnBreak]);

  try {
    console.log("[HEADER_RENDER] Admin check:", { hasAdminRole, roles, isDropdownOpen });
    return (
      <div className="header-container">
        <div className="header-main">
          {hasAdminRole && (
            <div className="admin-dropdown">
              <button
                className="dropdown-toggle"
                onClick={() => setIsDropdownOpen(!isDropdownOpen)}
              >
                Admin
              </button>
              {isDropdownOpen && (
                <ul className="dropdown-menu">
                  {(roles.includes("Application Manager") || roles.includes("Karaoke DJ") || roles.includes("Song Manager")) && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/admin/add-requests")}
                    >
                      Add Song Requests
                    </li>
                  )}
                  {(roles.includes("Song Manager") || roles.includes("Queue Manager") || roles.includes("Application Manager")) && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/song-manager")}
                    >
                      Manage Songs
                    </li>
                  )}
                  {(roles.includes("User Manager") || roles.includes("Application Manager")) && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/user-management")}
                    >
                      Manage Users
                    </li>
                  )}
                  {(roles.includes("Event Manager") || roles.includes("Application Manager")) && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/event-management")}
                    >
                      Manage Events
                    </li>
                  )}
                </ul>
              )}
            </div>
          )}
          <span
            className="header-user"
            onClick={() => handleNavigation("/profile")}
            style={{ cursor: "pointer" }}
          >
            Hello, {firstName || lastName ? `${firstName} ${lastName}`.trim() : "User"}!
          </span>
          {fetchError && <span className="error-text">{fetchError}</span>}
          {currentEvent && (
            <div className="event-status">
              <span className="event-name">
                {checkedIn ? `Checked into: ${currentEvent.description}` : `Pre-Selecting for: ${currentEvent.description}`}
              </span>
              {checkedIn && isCurrentEventLive && (
                <>
                  <button
                    className={isOnBreak ? "back-button" : "break-button"}
                    onClick={handleBreakToggle}
                    disabled={isCheckingIn}
                  >
                    {isOnBreak ? "I'm Back" : "Go On Break"}
                  </button>
                  <button
                    className="leave-event-button"
                    onClick={handleLeaveEvent}
                    disabled={isCheckingIn}
                  >
                    Leave Event
                  </button>
                </>
              )}
            </div>
          )}
          {!currentEvent && (
            <div className="event-actions">
              {isLoadingEvents ? (
                <span>Loading events...</span>
              ) : (
                <>
                  <div className="event-dropdown preselect-dropdown" ref={preselectDropdownRef}>
                    <button
                      className="preselect-button"
                      onClick={() => setIsPreselectDropdownOpen(!isPreselectDropdownOpen)}
                      disabled={upcomingEvents.length === 0}
                      aria-label="Pre-Select Songs for Upcoming Events"
                    >
                      Pre-Select
                    </button>
                    {isPreselectDropdownOpen && upcomingEvents.length > 0 && (
                      <ul className="event-dropdown-menu">
                        {upcomingEvents.map(event => (
                          <li
                            key={event.eventId}
                            className="event-dropdown-item"
                            onClick={() => handlePreselectSongs(event)}
                          >
                            {event.description} (Upcoming)
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                  <div className="event-dropdown join-event-dropdown" ref={eventDropdownRef}>
                    <button
                      className="check-in-button"
                      onClick={() => setIsEventDropdownOpen(!isEventDropdownOpen)}
                      disabled={isCheckingIn || liveEvents.length === 0}
                      aria-label="Join Live Event"
                    >
                      {isCheckingIn ? "Joining..." : "Join Event"}
                    </button>
                    {isEventDropdownOpen && (
                      <ul className="event-dropdown-menu">
                        {checkInError && (
                          <li className="event-dropdown-error">
                            {checkInError}
                          </li>
                        )}
                        {liveEvents.length === 0 ? (
                          <li className="event-dropdown-item">No live events available</li>
                        ) : (
                          liveEvents.map(event => (
                            <li
                              key={event.eventId}
                              className="event-dropdown-item"
                              onClick={() => handleCheckIn(event)}
                            >
                              {event.description} (Live)
                            </li>
                          ))
                        )}
                      </ul>
                    )}
                  </div>
                </>
              )}
            </div>
          )}
          <button className="logout-button" onClick={handleLogout} disabled={isCheckingIn}>
            <LogoutOutlined style={{ fontSize: '24px', marginRight: '8px' }} />
            Logout
          </button>
        </div>
        {checkInError && <p className="error-text">{checkInError}</p>}
        {showLeaveConfirmation && (
          <div className="confirmation-modal">
            <div className="confirmation-content">
              <h3>Confirm Leave Event</h3>
              <p>Are you sure you want to leave the event "{currentEvent?.description}"?</p>
              <div className="confirmation-buttons">
                <button onClick={confirmLeaveEvent} className="confirm-button">Yes, Leave</button>
                <button onClick={cancelLeaveEvent} className="cancel-button">Cancel</button>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  } catch (error) {
    console.error('[HEADER] Render error:', error);
    return <div>Error in Header: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
});

Header.displayName = "Header";

export default Header;