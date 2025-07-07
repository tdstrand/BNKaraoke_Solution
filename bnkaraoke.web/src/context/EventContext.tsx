// src/context/EventContext.tsx
import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { Event } from '../types';
import { API_ROUTES } from '../config/apiConfig';

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
}

const EventContext = createContext<EventContextType | undefined>(undefined);

const API_BASE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com' : 'http://localhost:7290';

export const EventContextProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const navigate = useNavigate();
  const [currentEvent, setCurrentEvent] = useState<Event | null>(() => {
    const storedEvent = localStorage.getItem("currentEvent");
    return storedEvent ? JSON.parse(storedEvent) : null;
  });
  const [checkedIn, setCheckedIn] = useState<boolean>(() => {
    const storedCheckedIn = localStorage.getItem("checkedIn");
    return storedCheckedIn === "true";
  });
  const [isCurrentEventLive, setIsCurrentEventLive] = useState<boolean>(() => {
    const storedIsLive = localStorage.getItem("isCurrentEventLive");
    return storedIsLive === "true";
  });
  const [isOnBreak, setIsOnBreak] = useState<boolean>(() => {
    const storedIsOnBreak = localStorage.getItem("isOnBreak");
    return storedIsOnBreak === "true";
  });
  const [liveEvents, setLiveEvents] = useState<Event[]>(() => {
    const storedLiveEvents = localStorage.getItem("liveEvents");
    return storedLiveEvents ? JSON.parse(storedLiveEvents) : [];
  });
  const [upcomingEvents, setUpcomingEvents] = useState<Event[]>(() => {
    const storedUpcomingEvents = localStorage.getItem("upcomingEvents");
    return storedUpcomingEvents ? JSON.parse(storedUpcomingEvents) : [];
  });
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Persist states to local storage
  useEffect(() => {
    if (currentEvent) {
      localStorage.setItem("currentEvent", JSON.stringify(currentEvent));
    } else {
      localStorage.removeItem("currentEvent");
    }
  }, [currentEvent]);

  useEffect(() => {
    localStorage.setItem("checkedIn", checkedIn.toString());
  }, [checkedIn]);

  useEffect(() => {
    localStorage.setItem("isCurrentEventLive", isCurrentEventLive.toString());
  }, [isCurrentEventLive]);

  useEffect(() => {
    localStorage.setItem("isOnBreak", isOnBreak.toString());
  }, [isOnBreak]);

  useEffect(() => {
    localStorage.setItem("liveEvents", JSON.stringify(liveEvents));
  }, [liveEvents]);

  useEffect(() => {
    localStorage.setItem("upcomingEvents", JSON.stringify(upcomingEvents));
  }, [upcomingEvents]);

  // Validate token
  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    const isLoginPage = ["/", "/register", "/change-password"].includes(window.location.pathname);
    console.log("[EVENT_CONTEXT] Validating token:", { token: !!token, userName: !!userName, isLoginPage });
    if (isLoginPage) {
      console.log("[EVENT_CONTEXT] Skipping token validation on login-related page");
      return null;
    }
    if (!token || !userName) {
      console.error("[EVENT_CONTEXT] No token or userName found", { token, userName });
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[EVENT_CONTEXT] Malformed token: does not contain three parts", { token });
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[EVENT_CONTEXT] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return null;
      }
      console.log("[EVENT_CONTEXT] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[EVENT_CONTEXT] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      navigate("/login");
      return null;
    }
  };

  // Fetch events
  useEffect(() => {
    const fetchEvents = async () => {
      const token = validateToken();
      if (!token) return;

      try {
        console.log("[EVENT_CONTEXT] Fetching events from:", `${API_BASE_URL}/api/events`);
        const response = await fetch(`${API_BASE_URL}/api/events`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("[EVENT_CONTEXT] Events response:", { status: response.status, body: responseText });
        if (!response.ok) {
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch events failed: ${response.status} - ${responseText}`);
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

        if (!currentEvent && !checkedIn) {
          if (live.length > 0) {
            setCurrentEvent(live[0]);
            setIsCurrentEventLive(true);
            console.log("[EVENT_CONTEXT] Auto-selected live event:", live[0]?.eventId);
          } else if (upcoming.length === 1) {
            setCurrentEvent(upcoming[0]);
            setIsCurrentEventLive(false);
            console.log("[EVENT_CONTEXT] Auto-selected upcoming event:", upcoming[0]?.eventId);
          } else {
            setCurrentEvent(null);
            setIsCurrentEventLive(false);
          }
        }
      } catch (err) {
        console.error("[EVENT_CONTEXT] Fetch events error:", err);
        setFetchError(err instanceof Error ? err.message : "Failed to load events");
        setLiveEvents([]);
        setUpcomingEvents([]);
      }
    };

    fetchEvents();
  }, [navigate, currentEvent, checkedIn]);

  // Fetch attendance status with caching
  useEffect(() => {
    const fetchAttendanceStatus = async () => {
      if (!currentEvent) {
        setCheckedIn(false);
        setIsCurrentEventLive(false);
        setIsOnBreak(false);
        return;
      }

      const token = validateToken();
      if (!token) return;

      const cacheKey = `attendanceStatus_${currentEvent.eventId}`;
      const cacheTimestampKey = `attendanceStatusTimestamp_${currentEvent.eventId}`;
      const cachedStatus = localStorage.getItem(cacheKey);
      const cachedTimestamp = localStorage.getItem(cacheTimestampKey);
      const cacheDuration = 60 * 1000; // 1 minute in milliseconds
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

      try {
        console.log(`[EVENT_CONTEXT] Fetching attendance status for event ${currentEvent.eventId}`);
        const response = await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/status`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("[EVENT_CONTEXT] Attendance Status Response:", response.status, responseText);
        if (!response.ok) {
          if (response.status === 401) {
            setFetchError("Session expired. Please log in again.");
            navigate("/login");
            return;
          }
          throw new Error(`Fetch attendance status failed: ${response.status} - ${responseText}`);
        }
        const data = JSON.parse(responseText);
        setCheckedIn(data.isCheckedIn || false);
        setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
        setIsOnBreak(data.isOnBreak || false);
        localStorage.setItem(cacheKey, JSON.stringify(data));
        localStorage.setItem(cacheTimestampKey, now.toString());
      } catch (err) {
        console.error("[EVENT_CONTEXT] Fetch attendance status error:", err);
        setCheckedIn(false);
        setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
        setIsOnBreak(false);
      }
    };

    fetchAttendanceStatus();
  }, [currentEvent, navigate]);

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