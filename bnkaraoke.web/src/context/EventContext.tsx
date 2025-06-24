import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { Event } from '../types';
import { API_ROUTES } from '../config/apiConfig';

interface EventContextType {
  currentEvent: Event | null;
  setCurrentEvent: (event: Event | null) => void;
  checkedIn: boolean;
  setCheckedIn: (value: boolean) => void;
  isCurrentEventLive: boolean;
  setIsCurrentEventLive: (value: boolean) => void;
  isOnBreak: boolean;
  setIsOnBreak: (value: boolean) => void;
}

const EventContext = createContext<EventContextType | undefined>(undefined);

export const EventContextProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
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

  // Fetch attendance status when currentEvent changes
  useEffect(() => {
    const fetchAttendanceStatus = async () => {
      if (!currentEvent) {
        setCheckedIn(false);
        setIsCurrentEventLive(false);
        setIsOnBreak(false);
        return;
      }

      const token = localStorage.getItem("token");
      if (!token) {
        console.error("No token found");
        setCheckedIn(false);
        setIsCurrentEventLive(false);
        setIsOnBreak(false);
        return;
      }

      try {
        console.log(`Fetching attendance status for event ${currentEvent.eventId}`);
        const response = await fetch(`${API_ROUTES.EVENTS}/${currentEvent.eventId}/attendance/status`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const responseText = await response.text();
        console.log("Attendance Status Response:", response.status, responseText);
        if (!response.ok) {
          if (response.status === 401) {
            localStorage.removeItem("token");
            setCheckedIn(false);
            setIsCurrentEventLive(false);
            setIsOnBreak(false);
            return;
          }
          throw new Error(`Fetch attendance status failed: ${response.status} - ${responseText}`);
        }
        const data = JSON.parse(responseText);
        setCheckedIn(data.isCheckedIn || false);
        setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
        setIsOnBreak(data.isOnBreak || false);
      } catch (err) {
        console.error("Fetch attendance status error:", err);
        setCheckedIn(false);
        setIsCurrentEventLive(currentEvent.status.toLowerCase() === "live");
        setIsOnBreak(false);
      }
    };

    fetchAttendanceStatus();
  }, [currentEvent]);

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
      }}
    >
      {children}
    </EventContext.Provider>
  );
};

const useEventContext = () => {
  const context = useContext(EventContext);
  if (!context) {
    throw new Error('useEventContext must be used within an EventContextProvider');
  }
  return context;
};

export default useEventContext;