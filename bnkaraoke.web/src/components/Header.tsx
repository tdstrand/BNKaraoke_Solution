import React, { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { LogoutOutlined } from '@ant-design/icons';
import "./Header.css";
import { API_ROUTES } from "../config/apiConfig";
import useEventContext from "../context/EventContext";
import { AttendanceAction, Event } from "../types";

const Header: React.FC = () => {
  console.log("[HEADER] Rendering");

  const navigate = useNavigate();
  const { currentEvent, setCurrentEvent, checkedIn, setCheckedIn, isCurrentEventLive, setIsCurrentEventLive } = useEventContext();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [roles, setRoles] = useState<string[]>([]);
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const [liveEvents, setLiveEvents] = useState<Event[]>([]);
  const [upcomingEvents, setUpcomingEvents] = useState<Event[]>([]);
  const [isEventDropdownOpen, setIsEventDropdownOpen] = useState(false);
  const [isPreselectDropdownOpen, setIsPreselectDropdownOpen] = useState(false);
  const [isCheckingIn, setIsCheckingIn] = useState(false);
  const [checkInError, setCheckInError] = useState<string | null>(null);
  const [isLoadingEvents, setIsLoadingEvents] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [showLeaveConfirmation, setShowLeaveConfirmation] = useState(false);
  const [isOnBreak, setIsOnBreak] = useState(false);
  const eventDropdownRef = useRef<HTMLDivElement>(null);
  const preselectDropdownRef = useRef<HTMLDivElement>(null);
  const [hasAttemptedCheckIn, setHasAttemptedCheckIn] = useState<boolean>(false);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[VALIDATE_TOKEN] No token or userName found");
      setFetchError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[VALIDATE_TOKEN] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFetchError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[VALIDATE_TOKEN] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setFetchError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[VALIDATE_TOKEN] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[VALIDATE_TOKEN] Error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setFetchError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  const token = localStorage.getItem("token");
  const userName = localStorage.getItem("userName");
  if (!token || !userName) {
    console.log("[HEADER] Skipped rendering: no token or userName");
    return null;
  }

  useEffect(() => {
    const fetchUserDetails = async () => {
      const token = validateToken();
      if (!token) return;

      console.log("[FETCH_USER_DETAILS] token=", token.slice(0, 10), "...", "userName=", userName);
      try {
        console.log(`[FETCH_USER_DETAILS] Fetching from: ${API_ROUTES.USER_DETAILS}`);
        const response = await fetch(API_ROUTES.USER_DETAILS, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("[FETCH_USER_DETAILS] Response:", { status: response.status, body: responseText });
        if (!response.ok) {
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          throw new Error(`Failed to fetch user details: ${response.status} - ${responseText}`);
        }
        let data;
        try {
          data = JSON.parse(responseText);
        } catch (jsonError) {
          console.error("[FETCH_USER_DETAILS] JSON parse error:", jsonError, "Raw response:", responseText);
          throw new Error("Invalid user details response format");
        }
        setFirstName(data.firstName || "");
        setLastName(data.lastName || "");
        setRoles(data.roles || []);
        localStorage.setItem("firstName", data.firstName || "");
        localStorage.setItem("lastName", data.lastName || "");
        localStorage.setItem("roles", JSON.stringify(data.roles || []));
      } catch (err) {
        console.error("[FETCH_USER_DETAILS] Error:", err);
        setFirstName(localStorage.getItem("firstName") || "");
        setLastName(localStorage.getItem("lastName") || "");
        const storedRoles = localStorage.getItem("roles");
        if (storedRoles) {
          try {
            setRoles(JSON.parse(storedRoles) || []);
          } catch (parseErr) {
            console.error("[FETCH_USER_DETAILS] Parse Roles Error:", parseErr);
          }
        }
        setFetchError(err instanceof Error ? err.message : "Failed to load user details");
      }
    };

    fetchUserDetails();
  }, [navigate]);

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
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, []);

  useEffect(() => {
    const fetchEvents = async () => {
      const token = validateToken();
      if (!token) return;

      console.log("[FETCH_EVENTS] token=", token.slice(0, 10), "...", "userName=", userName);
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
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch events failed: ${response.status} - ${responseText}`);
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

        if (!currentEvent && !checkedIn) {
          if (live.length > 0) {
            setCurrentEvent(live[0]); // Auto-select first live event
            setIsCurrentEventLive(true);
            console.log("[FETCH_EVENTS] Auto-selected live event:", live[0]?.eventId);
          } else if (upcoming.length === 1) {
            setCurrentEvent(upcoming[0]);
            setIsCurrentEventLive(false);
            console.log("[FETCH_EVENTS] Auto-selected upcoming event:", upcoming[0]?.eventId);
          } else {
            setCurrentEvent(null);
            setIsCurrentEventLive(false);
          }
        }
      } catch (err) {
        console.error("[FETCH_EVENTS] Error:", err);
        setFetchError(err instanceof Error ? err.message : "Failed to load events");
        setLiveEvents([]);
        setUpcomingEvents([]);
      } finally {
        setIsLoadingEvents(false);
        console.log("[FETCH_EVENTS] Completed: isLoadingEvents=false", { liveEvents, upcomingEvents });
      }
    };

    fetchEvents();
  }, [currentEvent, checkedIn, setCurrentEvent, setIsCurrentEventLive, navigate]);

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
      hasAttemptedCheckIn,
    });
  }, [checkedIn, isCurrentEventLive, currentEvent, isOnBreak, isLoadingEvents, liveEvents, upcomingEvents, isEventDropdownOpen]);

  const adminRoles = ["Song Manager", "User Manager", "Event Manager"];
  const hasAdminRole = roles.some(role => adminRoles.includes(role));
  const fullName = firstName || lastName ? `${firstName} ${lastName}`.trim() : "User";
  const recentlyLeftEvent = localStorage.getItem("recentlyLeftEvent");
  const canCheckIn = currentEvent && !checkedIn && currentEvent.eventId.toString() !== recentlyLeftEvent;

  const handleNavigation = (path: string) => {
    try {
      setIsDropdownOpen(false);
      navigate(path);
    } catch (err) {
      console.error("[HANDLE_NAVIGATION] Error:", err);
    }
  };

  const handleLogout = () => {
    try {
      console.log("[LOGOUT] Button clicked");
      localStorage.clear();
      setCurrentEvent(null);
      setCheckedIn(false);
      setIsCurrentEventLive(false);
      setIsOnBreak(false);
      setHasAttemptedCheckIn(false);
      navigate("/login");
    } catch (err) {
      console.error("[LOGOUT] Error:", err);
    }
  };

  const checkAttendanceStatus = async (event: Event) => {
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
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return false;
        }
        throw new Error(`Failed to fetch attendance status: ${response.status} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      if (data.isCheckedIn) {
        console.log(`[CHECK_ATTENDANCE] User already checked in for event ${event.eventId}`);
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
        setIsOnBreak(data.isOnBreak || false);
        setIsEventDropdownOpen(false);
        setHasAttemptedCheckIn(true);
        navigate("/dashboard");
        return true;
      }
      return false;
    } catch (err) {
      console.error("[CHECK_ATTENDANCE] Error:", err);
      setCheckInError(err instanceof Error ? err.message : "Failed to check attendance status");
      return false;
    }
  };

  const handleCheckIn = async (event: Event) => {
    if (hasAttemptedCheckIn) {
      console.log(`[CHECK_IN] Skipped for event ${event.eventId}: already attempted`);
      return;
    }

    const token = validateToken();
    if (!token) return;

    const isAlreadyCheckedIn = await checkAttendanceStatus(event);
    if (isAlreadyCheckedIn) return;

    console.log("[CHECK_IN] token=", token.slice(0, 10), "...", "userName=", userName);
    setIsCheckingIn(true);
    setCheckInError(null);

    try {
      const requestData: AttendanceAction = { RequestorId: userName };
      console.log(`[CHECK_IN] Checking into event: ${event.eventId}, payload:`, JSON.stringify(requestData));
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
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
        } else if (response.status === 400 && responseText.includes("Requestor is already checked in")) {
          console.log(`[CHECK_IN] User already checked in for event ${event.eventId}, updating context`);
          setCurrentEvent(event);
          setCheckedIn(true);
          setIsCurrentEventLive(event.status.toLowerCase() === "live");
          setIsEventDropdownOpen(false);
          setHasAttemptedCheckIn(true);
          navigate("/dashboard");
        } else {
          setCheckInError(`Check-in failed: ${responseText || response.statusText}`);
        }
        return;
      }
      console.log(`[CHECK_IN] Success for event ${event.eventId}`);
      setCurrentEvent(event);
      setCheckedIn(true);
      setIsCurrentEventLive(event.status.toLowerCase() === "live");
      setIsEventDropdownOpen(false);
      setHasAttemptedCheckIn(true);
      navigate("/dashboard");
    } catch (err) {
      console.error("[CHECK_IN] Error:", err);
      setCheckInError("Failed to check in to the event. Please try again.");
      setHasAttemptedCheckIn(true);
    } finally {
      setIsCheckingIn(false);
    }
  };

  const handlePreselectSongs = (event: Event) => {
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
    }
  };

  const handleLeaveEvent = async () => {
    if (!currentEvent) {
      console.error("[LEAVE_EVENT] No current event selected");
      return;
    }
    setShowLeaveConfirmation(true);
  };

  const confirmLeaveEvent = async () => {
    const token = validateToken();
    if (!token || !currentEvent) {
      setShowLeaveConfirmation(false);
      return;
    }

    console.log("[LEAVE_EVENT] token=", token.slice(0, 10), "...", "userName=", userName);
    try {
      const requestData: AttendanceAction = { RequestorId: userName };
      console.log(`[LEAVE_EVENT] Leaving event: ${currentEvent.eventId}, payload:`, JSON.stringify(requestData));
      // Try both endpoints due to uncertainty
      const endpoint = `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/leave`;
      const fallbackEndpoint = `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/check-out`;
      let response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      let responseText = await response.text();
      console.log("[LEAVE_EVENT] Response (leave):", { status: response.status, body: responseText });
      if (!response.ok && response.status === 404) {
        console.log("[LEAVE_EVENT] Trying fallback endpoint:", fallbackEndpoint);
        response = await fetch(fallbackEndpoint, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(requestData),
        });
        responseText = await response.text();
        console.log("[LEAVE_EVENT] Response (check-out):", { status: response.status, body: responseText });
      }

      if (!response.ok) {
        console.error(`[LEAVE_EVENT] Failed for event ${currentEvent.eventId}: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
        } else {
          setCheckInError(`Failed to leave event: ${responseText || response.statusText}`);
        }
        return;
      }
      console.log(`[LEAVE_EVENT] Success: ${currentEvent.eventId}`);
      localStorage.setItem("recentlyLeftEvent", currentEvent.eventId.toString());
      setCurrentEvent(null);
      setCheckedIn(false);
      setIsCurrentEventLive(false);
      setIsOnBreak(false);
      setShowLeaveConfirmation(false);
      setHasAttemptedCheckIn(false);
      navigate("/dashboard");
    } catch (err) {
      console.error("[LEAVE_EVENT] Error:", err);
      setCheckInError("Failed to leave the event. Please try again.");
    } finally {
      setShowLeaveConfirmation(false);
    }
  };

  const cancelLeaveEvent = () => {
    setShowLeaveConfirmation(false);
  };

  const handleBreakToggle = async () => {
    const token = validateToken();
    if (!token || !currentEvent) return;

    console.log("[BREAK_TOGGLE] token=", token.slice(0, 10), "...", "userName=", userName);
    try {
      const requestData: AttendanceAction = { RequestorId: userName };
      const endpoint = isOnBreak
        ? `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/return-from-break`
        : `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/take-break`;
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
        console.error(`[BREAK_TOGGLE] Failed for event ${currentEvent.eventId}: ${response.status} - ${responseText}`);
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
        } else {
          setCheckInError(`Failed to toggle break status: ${responseText || response.statusText}`);
        }
        return;
      }
      console.log(`[BREAK_TOGGLE] Success: ${isOnBreak ? 'Returned from break' : 'On break'}`);
      setIsOnBreak(!isOnBreak);
    } catch (err) {
      console.error("[BREAK_TOGGLE] Error:", err);
      setCheckInError("Failed to toggle break status. Please try again.");
    }
  };

  try {
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
                  {roles.includes("Song Manager") && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/song-manager")}
                    >
                      Manage Songs
                    </li>
                  )}
                  {roles.includes("User Manager") && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/user-management")}
                    >
                      Manage Users
                    </li>
                  )}
                  {roles.includes("Event Manager") && (
                    <li
                      className="dropdown-item"
                      onClick={() => handleNavigation("/event-manager")}
                    >
                      Manage Events
                    </li>
                  )}
                </ul>
              )}
            </div>
          )}
          <span className="header-user" onClick={() => handleNavigation("/profile")} style={{ cursor: "pointer" }}>
            Hello, {fullName}!
          </span>
          {fetchError && <span className="error-text">{fetchError}</span>}
          {currentEvent && (
            <div className="event-status">
              <span className="event-name">
                {checkedIn ? `Checked into: ${currentEvent.description}` : `Selected: ${currentEvent.description}`}
              </span>
              {checkedIn && isCurrentEventLive && (
                <>
                  <button className={isOnBreak ? "back-button" : "break-button"} onClick={handleBreakToggle}>
                    {isOnBreak ? "I'm Back" : "Go On Break"}
                  </button>
                  <button className="leave-event-button" onClick={handleLeaveEvent}>
                    Leave Event
                  </button>
                </>
              )}
              {canCheckIn && (
                <button
                  className="check-in-button"
                  onClick={() => handleCheckIn(currentEvent)}
                  disabled={isCheckingIn || hasAttemptedCheckIn}
                  aria-label="Check In to Event"
                >
                  {isCheckingIn ? "Checking In..." : "Check In"}
                </button>
              )}
            </div>
          )}
          {!currentEvent && (
            <div className="event-actions">
              {isLoadingEvents ? (
                <span>Loading events...</span>
              ) : (
                <>
                  {liveEvents.length === 0 && (
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
                              {event.description}
                            </li>
                          ))}
                        </ul>
                      )}
                    </div>
                  )}
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
                              {event.description}
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
          <button className="logout-button" onClick={handleLogout}>
            <LogoutOutlined style={{ fontSize: '24px', marginRight: '8px' }} />
            Logout
          </button>
        </div>
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
    console.error('[HEADER_RENDER] Error:', error);
    return <div>Error in Header: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default Header;