// src/context/EventContext.tsx
import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { Event, AttendanceAction } from '../types';
import { API_ROUTES } from '../config/apiConfig';
import toast from 'react-hot-toast';

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
}

const EventContext = createContext<EventContextType | undefined>(undefined);

const API_BASE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com' : 'http://localhost:7290';

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

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    const isLoginPage = ["/", "/register", "/change-password"].includes(location.pathname);
    console.log("[EVENT_CONTEXT] Validating token:", { token: !!token, userName: !!userName, isLoginPage });
    if (isLoginPage) {
      console.log("[EVENT_CONTEXT] Skipping token validation on login-related page");
      return null;
    }
    if (!token || !userName) {
      console.error("[EVENT_CONTEXT] No token or userName found", { token, userName });
      toast.error("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token!.split('.').length !== 3) {
        console.error("[EVENT_CONTEXT] Malformed token: does not contain three parts", { token });
        toast.error("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token!.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[EVENT_CONTEXT] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        toast.error("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[EVENT_CONTEXT] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[EVENT_CONTEXT] Token validation error:", err);
      toast.error("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  const checkAttendanceStatus = async (event: Event) => {
    const token = validateToken();
    if (!token) return { isCheckedIn: false, isOnBreak: false };

    // Skip navigation to /dashboard on admin pages
    const isAdminPage = location.pathname.startsWith('/admin') || ['/song-manager', '/user-management', '/event-management'].includes(location.pathname);
    console.log("[EVENT_CONTEXT] Checking attendance status:", { eventId: event.eventId, isAdminPage });

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
      if (data.isCheckedIn && !isAdminPage) {
        setCurrentEvent(event);
        setCheckedIn(true);
        setIsCurrentEventLive(event.status.toLowerCase() === "live");
        setIsOnBreak(data.isOnBreak || false);
        navigate("/dashboard");
        return { isCheckedIn: true, isOnBreak: data.isOnBreak || false };
      }
      return { isCheckedIn: false, isOnBreak: false };
    } catch (err) {
      console.error("[EVENT_CONTEXT] Error checking attendance status:", err);
      toast.error("Failed to check attendance status. Please try again.");
      return { isCheckedIn: false, isOnBreak: false };
    }
  };

  const fetchEvents = async () => {
    const token = validateToken();
    if (!token) return;

    // Reset event-related state and local storage
    localStorage.removeItem("currentEvent");
    localStorage.removeItem("checkedIn");
    localStorage.removeItem("isCurrentEventLive");
    localStorage.removeItem("isOnBreak");
    localStorage.removeItem("liveEvents");
    localStorage.removeItem("upcomingEvents");
    localStorage.removeItem("recentlyLeftEvent");
    localStorage.removeItem("recentlyLeftEventTimestamp");
    setCurrentEvent(null);
    setCheckedIn(false);
    setIsCurrentEventLive(false);
    setIsOnBreak(false);
    setLiveEvents([]);
    setUpcomingEvents([]);
    setFetchError(null);

    try {
      console.log("[EVENT_CONTEXT] Fetching events from:", `${API_BASE_URL}/api/events`);
      const response = await fetch(`${API_BASE_URL}/api/events`, {
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

      // Skip auto-check-in on admin pages
      const isAdminPage = location.pathname.startsWith('/admin') || ['/song-manager', '/user-management', '/event-management'].includes(location.pathname);
      if (isAdminPage) {
        console.log("[EVENT_CONTEXT] Skipping auto-check-in on admin page:", location.pathname);
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
      } else if (upcoming.length === 1 && live.length === 0) {
        console.log("[EVENT_CONTEXT] Auto-selected upcoming event:", upcoming[0]?.eventId);
        setCurrentEvent(upcoming[0]);
        setIsCurrentEventLive(false);
        setCheckedIn(false);
        setIsOnBreak(false);
      } else {
        console.log("[EVENT_CONTEXT] No events auto-selected, resetting state");
        setCurrentEvent(null);
        setIsCurrentEventLive(false);
        setCheckedIn(false);
        setIsOnBreak(false);
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
  };

  useEffect(() => {
    fetchEvents();
  }, [navigate, location.pathname]);

  useEffect(() => {
    const fetchAttendanceStatus = async () => {
      if (!currentEvent) {
        setCheckedIn(false);
        setIsCurrentEventLive(false);
        setIsOnBreak(false);
        return;
      }

      // Skip on admin pages
      const isAdminPage = location.pathname.startsWith('/admin') || ['/song-manager', '/user-management', '/event-management'].includes(location.pathname);
      if (isAdminPage) {
        console.log("[EVENT_CONTEXT] Skipping fetchAttendanceStatus on admin page:", location.pathname);
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
  }, [currentEvent, navigate, location.pathname]);

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
      }}
    >
      {fetchError && <div style={{ color: 'red', margin: '10px' }}>{fetchError}</div>}
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