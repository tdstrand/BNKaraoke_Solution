// src/context/EventContext.tsx
import React, { createContext, useContext, useState, useEffect, ReactNode, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Event, AttendanceAction } from '../types';
import { API_ROUTES } from '../config/apiConfig';
import toast from 'react-hot-toast';
import './EventContext.css';

interface EventContextType {
  currentEvent: Event | null;
  setCurrentEvent: React.Dispatch<React.SetStateAction<Event | null>>;
  checkedIn: boolean;
  setCheckedIn: React.Dispatch<React.SetStateAction<boolean>>;
  isCurrentEventLive: boolean;
  setIsCurrentEventLive: React.Dispatch<React.SetStateAction<boolean>>;
  isOnBreak: boolean;
  setIsOnBreak: React.Dispatch<React.SetStateAction<boolean>>;
  liveEvents: Event[];
  setLiveEvents: React.Dispatch<React.SetStateAction<Event[]>>;
  upcomingEvents: Event[];
  setUpcomingEvents: React.Dispatch<React.SetStateAction<Event[]>>;
  fetchError: string | null;
  setFetchError: React.Dispatch<React.SetStateAction<string | null>>;
  isLoggedIn: boolean;
  logout: (message?: string) => Promise<void>;
  selectionRequired: boolean; // New flag for selection UI
  setSelectionRequired: React.Dispatch<React.SetStateAction<boolean>>;
  noEvents: boolean; // New flag for no events case
  setNoEvents: React.Dispatch<React.SetStateAction<boolean>>;
}

const EventContext = createContext<EventContextType | undefined>(undefined);

export const EventContextProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const [currentEvent, setCurrentEvent] = useState<Event | null>(null);
  const [checkedIn, setCheckedIn] = useState<boolean>(false);
  const [isCurrentEventLive, setIsCurrentEventLive] = useState<boolean>(false);
  const [isOnBreak, setIsOnBreak] = useState<boolean>(false);
  const [liveEvents, setLiveEvents] = useState<Event[]>([]);
  const [upcomingEvents, setUpcomingEvents] = useState<Event[]>([]);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(localStorage.getItem('token') ? true : false);
  const [selectionRequired, setSelectionRequired] = useState<boolean>(false); // New state
  const [noEvents, setNoEvents] = useState<boolean>(false); // New state

  const logout = useCallback(async (message?: string) => {
    console.log("[LOGOUT] Logging out");
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");

    if (currentEvent && checkedIn && token && userName) {
      try {
        const requestData: AttendanceAction = { RequestorId: userName };
        await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/check-out`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify(requestData),
        });
      } catch (err) {
        console.error("[LOGOUT] Error leaving event:", err);
      }
    }

    if (token) {
      try {
        await fetch("/api/auth/logout", {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
        });
      } catch (err) {
        console.error("[LOGOUT] Error notifying API:", err);
      }
    }

    localStorage.clear();
    setIsLoggedIn(false);
    setCurrentEvent(null);
    setCheckedIn(false);
    setIsCurrentEventLive(false);
    setIsOnBreak(false);
    setLiveEvents([]);
    setUpcomingEvents([]);
    setFetchError(null);
    setSelectionRequired(false);
    setNoEvents(false);
    navigate("/login");
    if (message) {
      toast.error(message);
    } else {
      toast.success("Logged out successfully!");
    }
  }, [currentEvent, checkedIn, navigate]);

  const validateToken = useCallback(() => {
    if (!isLoggedIn) return null;
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    const isLoginPage = ["/", "/login", "/register", "/change-password"].includes(location.pathname);
    console.log("[EVENT_CONTEXT] Validating token:", { token: !!token, userName: !!userName, isLoginPage });
    if (isLoginPage) {
      console.log("[EVENT_CONTEXT] Skipping token validation on login-related page");
      return null;
    }
    if (!token || !userName) {
      console.error("[EVENT_CONTEXT] No token or userName found", { token, userName });
      logout("Authentication token or username missing. Please log in again.");
      return null;
    }
    try {
      const tokenParts = token.split('.');
      if (tokenParts.length !== 3) {
        console.error("[EVENT_CONTEXT] Malformed token: does not contain three parts");
        logout("Invalid token format. Please log in again.");
        return null;
      }
      const payload = JSON.parse(atob(tokenParts[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[EVENT_CONTEXT] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        logout("Session expired. Please log in again.");
        return null;
      }
      console.log("[EVENT_CONTEXT] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[EVENT_CONTEXT] Token validation error:", err);
      logout("Invalid token. Please log in again.");
      return null;
    }
  }, [isLoggedIn, location.pathname, logout]);

  const checkAttendanceStatus = useCallback(async (event: Event) => {
    const token = validateToken();
    if (!token) return { isCheckedIn: false, isOnBreak: false };
    const isRestrictedPage = location.pathname.startsWith('/admin') || [
      '/song-manager', '/user-management', '/event-management',
      '/explore-songs', '/profile', '/request-song', '/spotify-search',
      '/karaoke-channels', '/pending-requests', '/add-requests'
    ].includes(location.pathname);
    console.log("[EVENT_CONTEXT] Checking attendance status:", { eventId: event.eventId, isRestrictedPage });
    try {
      console.log(`[EVENT_CONTEXT] Fetching attendance status for event ${event.eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[EVENT_CONTEXT] Attendance Status Response:", { status: response.status, body: responseText });
      if (!response.ok) {
        if (response.status === 401) {
          console.error("[EVENT_CONTEXT] Unauthorized, navigating to login");
          toast.error("Session expired. Please log in again.");
          navigate("/login");
          return { isCheckedIn: false, isOnBreak: false };
        }
        throw new Error(`Failed to fetch attendance status: ${response.status} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      console.log(`[EVENT_CONTEXT] Attendance status for event ${event.eventId}:`, data);
      if (data.isCheckedIn && !isRestrictedPage) {
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
        setIsOnBreak(data.isOnBreak || false);
        if (!isRestrictedPage) navigate("/dashboard");
        return { isCheckedIn: true, isOnBreak: data.isOnBreak || false };
      }
      return { isCheckedIn: false, isOnBreak: false };
    } catch (err) {
      console.error("[EVENT_CONTEXT] Error checking attendance status:", err);
      toast.error("Failed to check attendance status. Please try again.");
      return { isCheckedIn: false, isOnBreak: false };
    }
  }, [validateToken, location.pathname, navigate]);

  const fetchEvents = useCallback(async () => {
    const token = validateToken();
    if (!token) return;

    // Reset fetch-related state before loading events
    setFetchError(null);
    setSelectionRequired(false);
    setNoEvents(false);

    try {
      console.log("[EVENT_CONTEXT] Fetching events from:", API_ROUTES.EVENTS);
      const response = await fetch(API_ROUTES.EVENTS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[EVENT_CONTEXT] Events response:", { status: response.status, body: responseText });
      if (!response.ok) {
        const errorMessage = response.status === 401
          ? "Session expired. Please log in again."
          : response.status === 403
          ? "Unable to fetch events due to authorization error. Please contact support."
          : `Failed to fetch events: ${response.status} - ${responseText}`;
        throw new Error(errorMessage);
      }
      let eventsData: Event[];
      try {
        eventsData = JSON.parse(responseText);
      } catch (jsonError) {
        console.error("[EVENT_CONTEXT] JSON parse error:", jsonError, "Raw response:", responseText);
        throw new Error("Invalid events response format");
      }
      console.log("[EVENT_CONTEXT] Fetched events:", eventsData);
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
      console.log("[EVENT_CONTEXT] Live events:", live);
      console.log("[EVENT_CONTEXT] Upcoming events:", upcoming);

      const isRestrictedPage = location.pathname.startsWith('/admin') || [
        '/song-manager', '/user-management', '/event-management',
        '/explore-songs', '/profile', '/request-song', '/spotify-search',
        '/karaoke-channels', '/pending-requests', '/add-requests'
      ].includes(location.pathname);
      if (isRestrictedPage) {
        console.log("[EVENT_CONTEXT] Skipping auto-check-in on restricted page:", location.pathname);
        return;
      }

      if (live.length === 1) {
        console.log("[EVENT_CONTEXT] Auto-selected live event:", live[0]?.eventId);
        setCurrentEvent(live[0]);
        setIsCurrentEventLive(true);
        const { isCheckedIn, isOnBreak } = await checkAttendanceStatus(live[0]);
        if (!isCheckedIn) {
          const requestData: AttendanceAction = { RequestorId: localStorage.getItem("userName") || "" };
          console.log(`[CHECK_IN] Attempting auto-check-in for event ${live[0].eventId}, payload:`, JSON.stringify(requestData));
          try {
            const response = await fetch(`${API_ROUTES.EVENTS}/${live[0].eventId}/attendance/check-in`, {
              method: 'POST',
              headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json',
              },
              body: JSON.stringify(requestData),
            });
            const responseText = await response.text();
            console.log("[CHECK_IN] Auto-check-in response:", { status: response.status, body: responseText });
            if (response.ok) {
              setCheckedIn(true);
              setIsOnBreak(false);
              navigate("/dashboard");
              toast.success(`Checked into event ${live[0].description} successfully!`);
            } else {
              console.error(`[CHECK_IN] Auto-check-in failed: ${response.status} - ${responseText}`);
              toast.error("Failed to auto-check-in. Please try manually.");
              if (response.status === 401) {
                navigate("/login");
              }
            }
          } catch (err) {
            console.error("[CHECK_IN] Auto-check-in error:", err);
            toast.error("Failed to auto-check-in. Please try manually.");
          }
        } else {
          setCheckedIn(true);
          setIsOnBreak(isOnBreak);
        }
      } else if (live.length > 1) {
        console.log("[EVENT_CONTEXT] Multiple live events detected, requiring selection:", live.map(e => e.eventId));
        setSelectionRequired(true); // Trigger selection UI
      } else if (live.length === 0 && upcoming.length === 1) {
        console.log("[EVENT_CONTEXT] Auto-selected upcoming event:", upcoming[0]?.eventId);
        setCurrentEvent(upcoming[0]);
        setIsCurrentEventLive(false);
        setCheckedIn(false);
        setIsOnBreak(false);
      } else if (live.length === 0 && upcoming.length > 1) {
        console.log("[EVENT_CONTEXT] Multiple upcoming events detected, requiring preselection:", upcoming.map(e => e.eventId));
        setSelectionRequired(true); // Trigger preselect UI
      } else {
        console.log("[EVENT_CONTEXT] No events available, disabling join/preselect");
        setNoEvents(true); // Disable join/preselect button
      }
    } catch (err) {
      console.error("[EVENT_CONTEXT] Fetch events error:", err);
      const errorMessage = err instanceof Error ? err.message : "Failed to load events";
      setFetchError(errorMessage);
      setLiveEvents([]);
      setUpcomingEvents([]);
      toast.error(errorMessage);
      if (errorMessage.includes("Session expired")) {
        navigate("/login");
      }
    }
  }, [validateToken, navigate, checkAttendanceStatus, location.pathname]);

  useEffect(() => {
    fetchEvents();
  }, [fetchEvents, location.pathname]);

  useEffect(() => {
    const fetchAttendanceStatus = async () => {
      if (!currentEvent) {
        setCheckedIn(false);
        setIsCurrentEventLive(false);
        setIsOnBreak(false);
        return;
      }
      const isRestrictedPage = location.pathname.startsWith('/admin') || [
        '/song-manager', '/user-management', '/event-management',
        '/explore-songs', '/profile', '/request-song', '/spotify-search',
        '/karaoke-channels', '/pending-requests', '/add-requests'
      ].includes(location.pathname);
      if (isRestrictedPage) {
        console.log("[EVENT_CONTEXT] Skipping fetchAttendanceStatus on restricted page:", location.pathname);
        return;
      }
      const token = validateToken();
      if (!token) return;
      const cacheKey = `attendanceStatus_${currentEvent.eventId}`;
      const cacheTimestampKey = `attendanceStatusTimestamp_${currentEvent.eventId}`;
      const cachedStatus = localStorage.getItem(cacheKey);
      const cachedTimestamp = localStorage.getItem(cacheTimestampKey);
      const cacheDuration = 60 * 1000;
      const now = Date.now();
      if (cachedStatus && cachedTimestamp && now - parseInt(cachedTimestamp, 10) < cacheDuration) {
        console.log(`[EVENT_CONTEXT] Using cached attendance status for event ${currentEvent.eventId}`);
        try {
          const data = JSON.parse(cachedStatus);
          setCheckedIn(data.isCheckedIn || false);
          setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
          setIsOnBreak(data.isOnBreak || false);
          return;
        } catch (jsonError) {
          console.error("[EVENT_CONTEXT] Error parsing cached attendance status:", jsonError);
        }
      }
      const { isCheckedIn, isOnBreak } = await checkAttendanceStatus(currentEvent);
      setCheckedIn(isCheckedIn);
      setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
      setIsOnBreak(isOnBreak);
      localStorage.setItem(cacheKey, JSON.stringify({ isCheckedIn, isOnBreak }));
      localStorage.setItem(cacheTimestampKey, now.toString());
    };
    fetchAttendanceStatus();
  }, [currentEvent, navigate, location.pathname, checkAttendanceStatus, validateToken]);

  return (
    <EventContext.Provider
      value={{
        currentEvent,
        setCurrentEvent,
        checkedIn,
        setCheckedIn,
        isCurrentEventLive,
        setIsCurrentEventLive,
        isOnBreak,
        setIsOnBreak,
        liveEvents,
        setLiveEvents,
        upcomingEvents,
        setUpcomingEvents,
        fetchError,
        setFetchError,
        isLoggedIn,
        logout,
        selectionRequired,
        setSelectionRequired,
        noEvents,
        setNoEvents,
      }}
    >
      {fetchError && (
        <div className="event-context-error mobile-event-context">
          {fetchError}
        </div>
      )}
      {children}
    </EventContext.Provider>
  );
};

export const useEventContext = () => {
  const context = useContext(EventContext);
  if (!context) {
    throw new Error('useEventContext must be used within an EventContextProvider');
  }
  return context;
};

export default EventContextProvider;