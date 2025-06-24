import React, { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { LogoutOutlined } from '@ant-design/icons';
import "./Header.css";
import { API_ROUTES } from "../config/apiConfig";
import useEventContext from "../context/EventContext";
import { AttendanceAction, Event } from "../types";

// Permanent fix for ESLint warnings and deployment reliability (May 2025)
const Header: React.FC = () => {
  console.log("Header component rendering");

  // Define all hooks before any early returns
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

  // Early guard after hooks
  const token = localStorage.getItem("token");
  const userName = localStorage.getItem("userName");
  if (!token || !userName) {
    console.log("Header skipped rendering logic: no token or userName");
    return null;
  }

  // Fetch user details on mount
  useEffect(() => {
    const fetchUserDetails = async () => {
      console.log("fetchUserDetails: token=", token, "userName=", userName);
      try {
        console.log(`Fetching user details from: ${API_ROUTES.USER_DETAILS}`);
        const response = await fetch(API_ROUTES.USER_DETAILS, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("User Details Response:", response.status, responseText);
        if (!response.ok) {
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/");
            return;
          }
          throw new Error(`Failed to fetch user details: ${response.status} - ${responseText}`);
        }
        const data = JSON.parse(responseText);
        setFirstName(data.firstName || "");
        setLastName(data.lastName || "");
        setRoles(data.roles || []);
        localStorage.setItem("firstName", data.firstName || "");
        localStorage.setItem("lastName", data.lastName || "");
        localStorage.setItem("roles", JSON.stringify(data.roles || []));
      } catch (err) {
        console.error("Fetch User Details Error:", err);
        setFirstName(localStorage.getItem("firstName") || "");
        setLastName(localStorage.getItem("lastName") || "");
        const storedRoles = localStorage.getItem("roles");
        if (storedRoles) {
          try {
            setRoles(JSON.parse(storedRoles) || []);
          } catch (parseErr) {
            console.error("Parse Roles Error:", parseErr);
          }
        }
        setFetchError(err instanceof Error ? err.message : "Unknown error");
      }
    };

    fetchUserDetails();
  }, [navigate, token, userName]);

  // Close dropdowns on outside click
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
        console.error("HandleClickOutside Error:", err);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
    };
  }, []);

  // Fetch events on mount
  useEffect(() => {
    console.log("fetchEvents: token=", token, "userName=", userName);
    const fetchEvents = async () => {
      try {
        setIsLoadingEvents(true);
        setFetchError(null);
        console.log(`Fetching events from: ${API_ROUTES.EVENTS}`);
        const response = await fetch(API_ROUTES.EVENTS, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("Events Response:", response.status, responseText);
        if (!response.ok) {
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/");
            return;
          }
          throw new Error(`Fetch events failed: ${response.status} - ${responseText}`);
        }
        const eventsData: Event[] = JSON.parse(responseText);
        console.log("Header - Fetched events:", eventsData);

        // Filter events
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
        console.log("Live events:", live);
        console.log("Upcoming events:", upcoming);

        // Auto-select logic: Only select upcoming events if no live events and not checked in
        if (!currentEvent && !checkedIn) {
          if (live.length > 0) {
            setCurrentEvent(null);
            setIsCurrentEventLive(false);
            console.log("Live events available, no auto-selection");
          } else if (upcoming.length === 1) {
            setCurrentEvent(upcoming[0]);
            setIsCurrentEventLive(false);
            console.log("Auto-selected upcoming event:", upcoming[0]?.eventId);
          } else {
            setCurrentEvent(null);
            setIsCurrentEventLive(false);
          }
        }
      } catch (err) {
        console.error("Header - Fetch events error:", err);
        setFetchError(err instanceof Error ? err.message : "Unknown error");
        setLiveEvents([]);
        setUpcomingEvents([]);
      } finally {
        setIsLoadingEvents(false);
      }
    };

    fetchEvents();
  }, [currentEvent, checkedIn, setCurrentEvent, setIsCurrentEventLive, navigate, token, userName]);

  // Log state for debugging
  useEffect(() => {
    console.log("Header - checkedIn:", checkedIn, "isCurrentEventLive:", isCurrentEventLive, "currentEvent:", currentEvent, "isOnBreak:", isOnBreak);
  }, [checkedIn, isCurrentEventLive, currentEvent, isOnBreak]);

  const adminRoles = ["Song Manager", "User Manager", "Event Manager"];
  const hasAdminRole = roles.some(role => adminRoles.includes(role));

  const handleNavigation = (path: string) => {
    try {
      setIsDropdownOpen(false);
      navigate(path);
    } catch (err) {
      console.error("HandleNavigation Error:", err);
    }
  };

  const handleLogout = () => {
    try {
      console.log("Logout button clicked");
      localStorage.clear();
      setCurrentEvent(null);
      setCheckedIn(false);
      setIsCurrentEventLive(false);
      setIsOnBreak(false);
      navigate("/");
    } catch (err) {
      console.error("HandleLogout Error:", err);
    }
  };

  const handleCheckIn = async (event: Event) => {
    console.log("handleCheckIn: token=", token, "userName=", userName);
    if (!token || !userName) {
      console.error("No token or userName found for check-in");
      setCheckInError("Please log in to join an event.");
      setIsEventDropdownOpen(false);
      navigate("/");
      return;
    }

    setIsCheckingIn(true);
    setCheckInError(null);

    try {
      const requestorId = userName;
      console.log(`Checking into event: ${event.eventId}, status: ${event.status}, requestorId: ${requestorId}`);
      const requestData: AttendanceAction = { RequestorId: requestorId };
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}/attendance/check-in`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log("Check-in Response:", response.status, responseText);
      if (!response.ok) {
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/");
          return;
        }
        if (response.status === 400 && responseText.includes("Requestor is already checked in")) {
          setCurrentEvent(event);
          setCheckedIn(true);
          setIsCurrentEventLive(true);
          setIsOnBreak(false);
          setIsEventDropdownOpen(false);
          navigate("/dashboard");
          console.log("User already checked in, updated context for event:", event.eventId);
          return;
        }
        if (response.status === 404) {
          setCheckInError("Check-in endpoint not found. Please contact support.");
          return;
        }
        throw new Error(`Check-in failed: ${response.status} - ${responseText}`);
      }

      setCurrentEvent(event);
      setCheckedIn(true);
      setIsCurrentEventLive(true);
      setIsOnBreak(false);
      setIsEventDropdownOpen(false);
      navigate("/dashboard");
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to check in";
      console.error("Check-in error:", errorMessage, err);
      setCheckInError(errorMessage);
      setIsEventDropdownOpen(true);
    } finally {
      setIsCheckingIn(false);
    }
  };

  const handlePreselectSongs = (event: Event) => {
    try {
      setCurrentEvent(event);
      setIsCurrentEventLive(false);
      setCheckedIn(false);
      setIsOnBreak(false);
      setIsPreselectDropdownOpen(false);
      navigate("/dashboard");
    } catch (err) {
      console.error("HandlePreselectSongs Error:", err);
    }
  };

  const handleLeaveEvent = () => {
    if (!currentEvent) {
      console.error("No current event selected");
      return;
    }
    setShowLeaveConfirmation(true);
  };

  const confirmLeaveEvent = async () => {
    if (!currentEvent) {
      console.error("No current event selected");
      setShowLeaveConfirmation(false);
      return;
    }

    try {
      console.log("confirmLeaveEvent: token=", token, "userName=", userName);
      if (!token || !userName) {
        console.error("No token or userName found for check-out");
        setCheckInError("Please log in to leave an event.");
        navigate("/");
        setShowLeaveConfirmation(false);
        return;
      }

      const requestorId = userName;
      console.log(`Checking out of event: ${currentEvent.eventId}, requestorId: ${requestorId}`);

      if (currentEvent.status.toLowerCase() === "live") {
        const requestData: AttendanceAction = { RequestorId: requestorId };
        const response = await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/check-out`, {
          method: 'POST',
          headers: {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json',
          },
          body: JSON.stringify(requestData),
        });

        const responseText = await response.text();
        console.log("Check-out Response:", response.status, responseText);
        if (!response.ok) {
          if (response.status === 401) {
            setCheckInError("Session expired. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/");
            setShowLeaveConfirmation(false);
            return;
          }
          if (response.status === 404) {
            setCheckInError("Check-out endpoint not found. Please contact support.");
            return;
          }
          throw new Error(`Check-out failed: ${response.status} - ${responseText}`);
        }
      }

      setCurrentEvent(null);
      setIsCurrentEventLive(false);
      setCheckedIn(false);
      setIsOnBreak(false);
      localStorage.setItem("recentlyLeftEvent", "true"); // Flag to prevent auto-check-in
      setTimeout(() => localStorage.removeItem("recentlyLeftEvent"), 10000); // Clear flag after 10 seconds
      navigate("/dashboard");
    } catch (err) {
      console.error("Check-out error:", err);
      setCheckInError(err instanceof Error ? err.message : "Failed to leave event");
    } finally {
      setShowLeaveConfirmation(false);
    }
  };

  const cancelLeaveEvent = () => {
    setShowLeaveConfirmation(false);
  };

  const handleBreakToggle = async () => {
    if (!currentEvent) {
      console.error("No current event selected");
      return;
    }

    console.log("handleBreakToggle: token=", token, "userName=", userName);
    if (!token || !userName) {
      console.error("No token or userName found for break toggle");
      setCheckInError("Please log in to toggle break status.");
      navigate("/");
      return;
    }

    try {
      const requestorId = userName;
      const endpoint = isOnBreak
        ? `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/break/end`
        : `${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/break/start`;
      const requestData: AttendanceAction = { RequestorId: requestorId };

      console.log(`Toggling break status for event ${currentEvent.eventId}, endpoint: ${endpoint}`);
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(requestData),
      });

      const responseText = await response.text();
      console.log("Break Toggle Response:", response.status, responseText);
      if (!response.ok) {
        if (response.status === 401) {
          setCheckInError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/");
          return;
        }
        if (response.status === 404) {
          setCheckInError("Break toggle endpoint not found. Please contact support.");
          return;
        }
        throw new Error(`Break toggle failed: ${response.status} - ${responseText}`);
      }

      setIsOnBreak(!isOnBreak);
      console.log(`User is now ${isOnBreak ? "off break" : "on break"}`);
    } catch (err) {
      console.error("Break toggle error:", err);
      setCheckInError(err instanceof Error ? err.message : "Failed to toggle break status");
    }
  };

  const fullName = firstName || lastName ? `${firstName} ${lastName}`.trim() : "User";

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
                {checkedIn ? `Checked into: ${currentEvent.eventCode}` : `Pre-Selecting for: ${currentEvent.eventCode}`}
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
                              {event.status}: {event.eventCode} ({event.scheduledDate})
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
                              {event.status}: {event.eventCode} ({event.scheduledDate})
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
              {/* eslint-disable-next-line react/no-unescaped-entities */}
              <p>Are you sure you want to leave the event \"{currentEvent?.eventCode}\"?</p>
              <button onClick={confirmLeaveEvent} className="confirm-button">Yes, Leave</button>
              <button onClick={cancelLeaveEvent} className="cancel-button">Cancel</button>
            </div>
          </div>
        )}
      </div>
    );
  } catch (error) {
    console.error('Header render error:', error);
    return <div>Error in Header: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default Header;