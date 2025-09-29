// src/pages/EventManagement.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./EventManagement.css";
import { Event } from "../types";

interface EventUpdate {
  eventId: number;
  eventCode: string;
  description?: string;
  status?: string;
  visibility?: string;
  location?: string;
  scheduledDate?: string;
  scheduledStartTime?: string;
  scheduledEndTime?: string;
  karaokeDJName?: string;
  isCanceled?: boolean;
  requestLimit?: number;
}

interface NewEvent {
  eventCode: string;
  description: string;
  status: string;
  visibility: string;
  location: string;
  scheduledDate: string;
  scheduledStartTime: string;
  duration: number; // In hours
  karaokeDJName: string;
  isCanceled: boolean;
  requestLimit: number;
}

const EventManagementPage: React.FC = () => {
  const navigate = useNavigate();
  const [events, setEvents] = useState<Event[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newEvent, setNewEvent] = useState<NewEvent>({
    eventCode: "",
    description: "",
    status: "Upcoming",
    visibility: "Visible",
    location: "",
    scheduledDate: "",
    scheduledStartTime: "",
    duration: 5,
    karaokeDJName: "",
    isCanceled: false,
    requestLimit: 5,
  });
  const [editEvent, setEditEvent] = useState<EventUpdate | null>(null);
  const [showAddEventModal, setShowAddEventModal] = useState(false);
  const [statusUpdateEventId, setStatusUpdateEventId] = useState<number | null>(null);
  const [deletingEventId, setDeletingEventId] = useState<number | null>(null);
  const statusOptions = [
    { label: 'Upcoming', value: 'Upcoming' },
    { label: 'Live', value: 'Live' },
    { label: 'Archived', value: 'Archived' },
  ];

  const formatDateOnly = (value?: string): string => {
    if (!value) return "";
    return value.includes("T") ? value.split("T")[0] : value;
  };

  const formatDisplayDate = (value?: string): string => {
    if (!value) return "TBD";
    const dateOnly = formatDateOnly(value);
    if (!dateOnly) return "TBD";
    try {
      return new Date(dateOnly).toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
      });
    } catch (err) {
      console.error('[EVENT_MANAGEMENT] Unable to format display date:', value, err);
      return dateOnly;
    }
  };

  const formatDisplayTime = (value?: string | null): string => {
    if (!value) return "TBD";
    const trimmed = value.trim();
    if (!trimmed) return "TBD";
    if (/^\d{1,2}:\d{2}:\d{2}$/.test(trimmed)) {
      return trimmed.substring(0, 5);
    }
    return trimmed;
  };

  const validateToken = useCallback(() => {
    console.log("[EVENT_MANAGEMENT] Validating token");
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[EVENT_MANAGEMENT] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[EVENT_MANAGEMENT] Malformed token: does not contain three parts");
        setError("Invalid token format. Please log in again.");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[EVENT_MANAGEMENT] Token expired:", { exp: new Date(exp).toISOString(), now: new Date().toISOString() });
        setError("Session expired. Please log in again.");
        return null;
      }
      console.log("[EVENT_MANAGEMENT] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[EVENT_MANAGEMENT] Token validation error:", err);
      setError("Invalid token. Please log in again.");
      return null;
    }
  }, []);

  const formatTime = (input: string): string => {
    try {
      const timeParts = input.match(/^(\d{1,2}):(\d{2})\s*(AM|PM)?$/i) || input.match(/^(\d{1,2}):(\d{2})$/i);
      if (!timeParts) {
        throw new Error('Invalid time format');
      }
      let hours = parseInt(timeParts[1], 10);
      const minutes = parseInt(timeParts[2], 10);
      const isPM = timeParts[3]?.toUpperCase() === 'PM';
      if (timeParts[3]) {
        if (hours < 1 || hours > 12) {
          throw new Error('Hours must be between 1 and 12 for AM/PM format');
        }
        if (isPM && hours !== 12) hours += 12;
        if (!isPM && hours === 12) hours = 0;
      } else if (hours > 23) {
        throw new Error('Start time hours must be between 0 and 23');
      }
      if (minutes > 59) {
        throw new Error('Minutes must be between 0 and 59');
      }
      const formattedTime = `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:00`;
      console.log('[EVENT_MANAGEMENT] Formatted time:', { input, hours, minutes, formattedTime });
      return formattedTime;
    } catch (err) {
      console.error('[EVENT_MANAGEMENT] Time format error:', input, err);
      throw err;
    }
  };

  const calculateEndTime = (startTime: string, durationHours: number): string => {
    try {
      const timeParts = startTime.match(/^(\d{1,2}):(\d{2})\s*(AM|PM)?$/i) || startTime.match(/^(\d{1,2}):(\d{2})$/i);
      if (!timeParts) {
        throw new Error('Invalid start time format');
      }
      let hours = parseInt(timeParts[1], 10);
      const minutes = parseInt(timeParts[2], 10);
      const isPM = timeParts[3]?.toUpperCase() === 'PM';
      if (timeParts[3]) {
        if (hours < 1 || hours > 12) {
          throw new Error('Hours must be between 1 and 12 for AM/PM format');
        }
        if (isPM && hours !== 12) hours += 12;
        if (!isPM && hours === 12) hours = 0;
      }
      const durationMinutes = Math.floor(durationHours * 60); // Convert hours to minutes, ensure integer
      const totalMinutes = hours * 60 + minutes + durationMinutes;
      const endHours = Math.floor(totalMinutes / 60); // Integer hours
      const endMinutes = totalMinutes % 60;
      if (endHours > 47 || (endHours === 47 && endMinutes > 59)) {
        throw new Error('Total event duration cannot exceed 47:59:59 (start time + duration)');
      }
      const formattedEndTime = `${endHours.toString().padStart(2, '0')}:${endMinutes.toString().padStart(2, '0')}:00`;
      console.log('[EVENT_MANAGEMENT] Calculated end time:', { startTime, durationHours, totalMinutes, endHours, endMinutes, formattedEndTime });
      return formattedEndTime;
    } catch (err) {
      console.error('[EVENT_MANAGEMENT] End time calculation error:', startTime, durationHours, err);
      throw err;
    }
  };

  const normalizeTimeFieldForUpdate = (value?: string | null): string | null => {
    if (!value) {
      return null;
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }

    if (/^\d{1,2}:\d{2}:\d{2}$/.test(trimmed)) {
      return trimmed;
    }

    if (/^\d{1,2}:\d{2}$/.test(trimmed) || /^\d{1,2}:\d{2}\s*(AM|PM)$/i.test(trimmed)) {
      try {
        return formatTime(trimmed);
      } catch (err) {
        console.error('[EVENT_MANAGEMENT] Unable to normalize time field:', { value, err });
        return trimmed;
      }
    }

    console.warn('[EVENT_MANAGEMENT] Unexpected time format encountered. Sending raw value.', value);
    return trimmed;
  };

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const storedRoles = localStorage.getItem("roles");
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("Event Manager") && !parsedRoles.includes("Application Manager")) {
        console.error("[EVENT_MANAGEMENT] User lacks Event Manager or Application Manager role, redirecting to dashboard");
        setError("You do not have permission to access event management. Event Manager or Application Manager role required.");
        navigate("/dashboard");
        return;
      }
    } else {
      console.error("[EVENT_MANAGEMENT] No roles found, redirecting to login");
      setError("No roles found. Please log in again.");
      navigate("/login");
      return;
    }

    fetchManageEvents(token);
  }, [navigate, validateToken]);

  const fetchManageEvents = async (token: string) => {
    try {
      console.log(`[EVENT_MANAGEMENT] Fetching events from: ${API_ROUTES.MANAGE_EVENTS}`);
      const response = await fetch(API_ROUTES.MANAGE_EVENTS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] Events Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to fetch events due to authorization error. Please contact support."
          : `Failed to fetch events: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      const data: Event[] = JSON.parse(responseText);
      setEvents(data);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to fetch events. Please try again.";
      setError(errorMessage);
      setEvents([]);
      console.error("[EVENT_MANAGEMENT] Fetch Events Error:", err);
    }
  };

  const createEvent = async () => {
    const token = validateToken();
    if (!token) return;

    if (!newEvent.eventCode || !newEvent.description || !newEvent.location || !newEvent.scheduledDate || !newEvent.scheduledStartTime || !newEvent.karaokeDJName) {
      console.error('[EVENT_MANAGEMENT] Missing required fields');
      setError('Please fill in all required fields: event code, description, location, date, start time, DJ name.');
      return;
    }

    if (newEvent.requestLimit < 1) {
      console.error('[EVENT_MANAGEMENT] Invalid request limit:', newEvent.requestLimit);
      setError('Request limit must be at least 1.');
      return;
    }

    if (newEvent.duration < 0.5 || newEvent.duration > 24) {
      console.error('[EVENT_MANAGEMENT] Invalid duration:', newEvent.duration);
      setError('Duration must be between 0.5 and 24 hours.');
      return;
    }

    let formattedStartTime = '';
    let formattedEndTime = '';
    try {
      formattedStartTime = formatTime(newEvent.scheduledStartTime);
      formattedEndTime = calculateEndTime(newEvent.scheduledStartTime, newEvent.duration);
    } catch (err) {
      console.error('[EVENT_MANAGEMENT] Invalid time or duration:', err);
      setError('Invalid start time or duration. Use HH:mm or h:mm AM/PM (e.g., "18:00" or "6:00 PM") for start time, and 0.5–24 hours for duration.');
      return;
    }

    const payload = {
      eventCode: newEvent.eventCode,
      description: newEvent.description,
      status: newEvent.status,
      visibility: newEvent.visibility,
      location: newEvent.location,
      scheduledDate: newEvent.scheduledDate,
      scheduledStartTime: formattedStartTime,
      scheduledEndTime: formattedEndTime,
      karaokeDJName: newEvent.karaokeDJName,
      isCanceled: newEvent.isCanceled,
      requestLimit: newEvent.requestLimit,
    };

    console.log('[EVENT_MANAGEMENT] Creating event with payload:', JSON.stringify(payload));

    try {
      const response = await fetch(API_ROUTES.CREATE_EVENT, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(payload),
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] Create Event Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to create event due to authorization error. Please contact support."
          : response.status === 400
            ? `Failed to create event: ${responseText || response.statusText}`
            : `Failed to create event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      const data = JSON.parse(responseText);
      alert(`Event created successfully! ID: ${data.eventId}`);
      setNewEvent({
        eventCode: "",
        description: "",
        status: "Upcoming",
        visibility: "Visible",
        location: "",
        scheduledDate: "",
        scheduledStartTime: "",
        duration: 5,
        karaokeDJName: "",
        isCanceled: false,
        requestLimit: 5,
      });
      setShowAddEventModal(false);
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to create event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] Create Event Error:", err);
    }
  };

  const updateEvent = async () => {
    if (!editEvent) return;
    const token = validateToken();
    if (!token) return;

    if (!editEvent.eventCode || !editEvent.description || !editEvent.location || !editEvent.scheduledDate || !editEvent.scheduledStartTime || !editEvent.karaokeDJName) {
      console.error('[EVENT_MANAGEMENT] Missing required fields for update');
      setError('Please fill in all required fields: event code, description, location, date, start time, DJ name.');
      return;
    }

    let formattedStartTime = '';
    let formattedEndTime = '';
    try {
      formattedStartTime = formatTime(editEvent.scheduledStartTime || '');
      formattedEndTime = formatTime(editEvent.scheduledEndTime || '');
      if (parseInt(formattedEndTime.split(':')[0], 10) <= parseInt(formattedStartTime.split(':')[0], 10)) {
        console.error('[EVENT_MANAGEMENT] End time must be after start time');
        setError('End time must be after start time.');
        return;
      }
    } catch (err) {
      console.error('[EVENT_MANAGEMENT] Invalid time format for update:', err);
      setError('Invalid start or end time format. Use HH:mm or h:mm AM/PM (e.g., "18:00" or "6:00 PM").');
      return;
    }

    const formattedDate = formatDateOnly(editEvent.scheduledDate);
    if (!formattedDate) {
      console.error('[EVENT_MANAGEMENT] Invalid scheduled date for update:', editEvent.scheduledDate);
      setError('Please provide a valid scheduled date before saving.');
      return;
    }

    const payload = {
      eventId: editEvent.eventId,
      eventCode: editEvent.eventCode,
      description: editEvent.description,
      status: editEvent.status,
      visibility: editEvent.visibility,
      location: editEvent.location,
      scheduledDate: formattedDate,
      scheduledStartTime: formattedStartTime,
      scheduledEndTime: formattedEndTime,
      karaokeDJName: editEvent.karaokeDJName,
      isCanceled: editEvent.isCanceled,
      requestLimit: editEvent.requestLimit,
    };

    console.log('[EVENT_MANAGEMENT] Updating event with payload:', JSON.stringify(payload));

    try {
      const response = await fetch(`${API_ROUTES.EVENTS}/${editEvent.eventId}/update`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(payload),
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] Update Event Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to update event due to authorization error. Please contact support."
          : `Failed to update event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      alert("Event updated successfully!");
      setEditEvent(null);
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to update event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] Update Event Error:", err);
    }
  };

  const updateEventStatus = async (event: Event, newStatus: string) => {
    const token = validateToken();
    if (!token) return;

    const formattedDate = formatDateOnly(event.scheduledDate);
    if (!formattedDate) {
      console.error('[EVENT_MANAGEMENT] Missing scheduled date for status update', event);
      setError('Unable to update event status because the scheduled date is missing.');
      return;
    }

    const payload = {
      eventCode: event.eventCode,
      description: event.description,
      status: newStatus,
      visibility: event.visibility,
      location: event.location,
      scheduledDate: formattedDate,
      scheduledStartTime: normalizeTimeFieldForUpdate(event.scheduledStartTime),
      scheduledEndTime: normalizeTimeFieldForUpdate(event.scheduledEndTime),
      karaokeDJName: event.karaokeDJName ?? '',
      isCanceled: event.isCanceled,
      requestLimit: event.requestLimit,
    };

    console.log('[EVENT_MANAGEMENT] Updating event status:', { eventId: event.eventId, payload });

    setStatusUpdateEventId(event.eventId);
    try {
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}/update`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(payload),
      });
      const responseText = await response.text();
      console.log('[EVENT_MANAGEMENT] Update Event Status Raw Response:', responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? 'Unable to update event due to authorization error. Please contact support.'
          : `Failed to update event status: ${responseText || response.statusText}`;
        throw new Error(errorMessage);
      }
      alert(`Event status updated to ${newStatus}!`);
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to update event status. Please try again.';
      setError(errorMessage);
      console.error('[EVENT_MANAGEMENT] Update Event Status Error:', err);
    } finally {
      setStatusUpdateEventId(null);
    }
  };

  const startEvent = async (eventId: number, eventStatus: string) => {
    if (eventStatus !== "Upcoming") {
      console.error(`[EVENT_MANAGEMENT] Cannot start event ${eventId}: status is ${eventStatus}, must be Upcoming`);
      setError(`Cannot start event: status is ${eventStatus}. Only Upcoming events can be started.`);
      return;
    }

    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[EVENT_MANAGEMENT] Starting event ${eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${eventId}/start`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] Start Event Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to start event due to authorization error. Please contact support."
          : response.status === 400
            ? `Failed to start event: ${responseText || response.statusText}`
            : `Failed to start event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      alert("Event started successfully!");
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to start event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] Start Event Error:", err);
    }
  };

  const endEvent = async (eventId: number, eventStatus: string) => {
    if (eventStatus === "Archived") {
      console.error(`[EVENT_MANAGEMENT] Cannot end event ${eventId}: status is Archived`);
      setError("Cannot end event: status is Archived.");
      return;
    }

    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[EVENT_MANAGEMENT] Ending event ${eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${eventId}/end`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] End Event Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to end event due to authorization error. Please contact support."
          : `Failed to end event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      alert("Event ended successfully!");
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to end event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] End Event Error:", err);
    }
  };

  const deleteEvent = async (event: Event) => {
    if (!window.confirm(`Are you sure you want to delete the event "${event.description}"? This action cannot be undone.`)) {
      return;
    }

    const token = validateToken();
    if (!token) return;

    setDeletingEventId(event.eventId);
    try {
      console.log(`[EVENT_MANAGEMENT] Deleting event ${event.eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${event.eventId}`, {
        method: 'DELETE',
        headers: {
          Authorization: `Bearer ${token}`,
        },
      });
      const responseText = await response.text();
      console.log('[EVENT_MANAGEMENT] Delete Event Raw Response:', responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? 'Unable to delete event due to authorization error. Please contact support.'
          : `Failed to delete event: ${responseText || response.statusText}`;
        throw new Error(errorMessage);
      }
      alert(`Event "${event.description}" deleted successfully.`);
      fetchManageEvents(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : 'Failed to delete event. Please try again.';
      setError(errorMessage);
      console.error('[EVENT_MANAGEMENT] Delete Event Error:', err);
    } finally {
      setDeletingEventId(null);
    }
  };

  const handleOpenAddEventModal = () => {
    setShowAddEventModal(true);
    setError(null);
    console.log("[EVENT_MANAGEMENT] Opening Add New Event modal");
  };

  try {
    return (
      <div className="event-management-container mobile-event-management">
        <header className="event-management-header">
          <h1 className="event-management-title">Event Management</h1>
          <div className="header-buttons">
            <button 
              className="action-button add-event-button" 
              onClick={handleOpenAddEventModal}
              onTouchStart={handleOpenAddEventModal}
            >
              Add New Event
            </button>
            <button 
              className="action-button back-button" 
              onClick={() => navigate("/dashboard")}
              onTouchStart={() => navigate("/dashboard")}
            >
              Back to Dashboard
            </button>
          </div>
        </header>
        <div className="card-container">
          <section className="event-management-card edit-events-card">
            <h2 className="section-title">Manage Events</h2>
            {error && <p className="error-text">{error}</p>}
            {events.length > 0 ? (
              <ul className="event-list">
                {events.map((event) => {
                  const isStatusUpdating = statusUpdateEventId === event.eventId;
                  const isDeleting = deletingEventId === event.eventId;
                  const isBusy = isStatusUpdating || isDeleting;
                  return (
                    <li key={event.eventId} className="event-item">
                      <div className="event-info">
                        <div className="event-header">
                          <p className="event-title">{event.description} ({event.eventCode})</p>
                          <span className={`status-pill status-${event.status.toLowerCase()}`}>{event.status}</span>
                        </div>
                        <p className="event-text">{event.location || 'No location provided'}</p>
                        <div className="event-meta-row">
                          <span className="event-meta-chip">Date: {formatDisplayDate(event.scheduledDate)}</span>
                          <span className="event-meta-chip">Start: {formatDisplayTime(event.scheduledStartTime)}</span>
                          <span className="event-meta-chip">End: {formatDisplayTime(event.scheduledEndTime)}</span>
                          <span className="event-meta-chip">Visibility: {event.visibility}</span>
                          <span className="event-meta-chip">Requests: {event.requestLimit}</span>
                          <span className="event-meta-chip">Queue Items: {event.queueCount}</span>
                        </div>
                      </div>
                      <div className="event-actions">
                        <div className="event-actions-row">
                          <button
                            className="action-button edit-button"
                            onClick={() => setEditEvent({ ...event, eventId: event.eventId, eventCode: event.eventCode })}
                            onTouchStart={() => setEditEvent({ ...event, eventId: event.eventId, eventCode: event.eventCode })}
                            disabled={event.status === "Archived" || isBusy}
                          >
                            Edit
                          </button>
                          <button
                            className="action-button start-button"
                            onClick={() => startEvent(event.eventId, event.status)}
                            onTouchStart={() => startEvent(event.eventId, event.status)}
                            disabled={event.status !== "Upcoming" || isBusy}
                          >
                            Start
                          </button>
                          <button
                            className="action-button end-button"
                            onClick={() => endEvent(event.eventId, event.status)}
                            onTouchStart={() => endEvent(event.eventId, event.status)}
                            disabled={event.status === "Archived" || isBusy}
                          >
                            End
                          </button>
                          <button
                            className="action-button danger-button delete-button"
                            onClick={() => deleteEvent(event)}
                            onTouchStart={() => deleteEvent(event)}
                            disabled={isBusy}
                          >
                            Delete
                          </button>
                        </div>
                        <div className="status-actions">
                          <span className="status-label">Set status:</span>
                          {statusOptions.map((option) => (
                            <button
                              key={option.value}
                              className={`action-button status-button${event.status === option.value ? ' active' : ''}`}
                              onClick={() => updateEventStatus(event, option.value)}
                              onTouchStart={() => updateEventStatus(event, option.value)}
                              disabled={isBusy || event.status === option.value}
                            >
                              {option.label}
                            </button>
                          ))}
                        </div>
                        {isStatusUpdating && <p className="event-action-note">Updating status...</p>}
                        {isDeleting && <p className="event-action-note">Deleting event...</p>}
                      </div>
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="event-management-text">{error ? "Failed to load events. Please try again or contact support." : "No events found."}</p>
            )}
          </section>
        </div>

        {showAddEventModal && (
          <div className="modal-overlay mobile-event-management">
            <div className="modal-content add-event-modal">
              <h2 className="modal-title">Add New Event</h2>
              <div className="add-event-form">
                <label className="form-label">Event Code</label>
                <input
                  type="text"
                  value={newEvent.eventCode}
                  onChange={(e) => setNewEvent({ ...newEvent, eventCode: e.target.value })}
                  placeholder="e.g., KARAOKE_20250707"
                  className="form-input"
                />
                <label className="form-label">Description</label>
                <input
                  type="text"
                  value={newEvent.description}
                  onChange={(e) => setNewEvent({ ...newEvent, description: e.target.value })}
                  placeholder="e.g., Karaoke Night"
                  className="form-input"
                />
                <label className="form-label">Location</label>
                <input
                  type="text"
                  value={newEvent.location}
                  onChange={(e) => setNewEvent({ ...newEvent, location: e.target.value })}
                  placeholder="e.g., Club XYZ"
                  className="form-input"
                />
                <label className="form-label">Scheduled Date</label>
                <input
                  type="date"
                  value={newEvent.scheduledDate}
                  onChange={(e) => setNewEvent({ ...newEvent, scheduledDate: e.target.value })}
                  className="form-input"
                />
                <label className="form-label">Start Time (e.g., 18:00 or 6:00 PM)</label>
                <input
                  type="text"
                  value={newEvent.scheduledStartTime}
                  onChange={(e) => setNewEvent({ ...newEvent, scheduledStartTime: e.target.value })}
                  placeholder="HH:mm or h:mm AM/PM"
                  className="form-input"
                />
                <label className="form-label">Duration (hours, 0.5–24)</label>
                <input
                  type="number"
                  step="0.5"
                  value={newEvent.duration}
                  onChange={(e) => setNewEvent({ ...newEvent, duration: parseFloat(e.target.value) || 5 })}
                  placeholder="e.g., 5"
                  className="form-input"
                  min="0.5"
                  max="24"
                />
                <label className="form-label">Karaoke DJ Name</label>
                <input
                  type="text"
                  value={newEvent.karaokeDJName}
                  onChange={(e) => setNewEvent({ ...newEvent, karaokeDJName: e.target.value })}
                  placeholder="e.g., DJ Groove"
                  className="form-input"
                />
                <label className="form-checkbox">
                  <input
                    type="checkbox"
                    checked={newEvent.isCanceled}
                    onChange={(e) => setNewEvent({ ...newEvent, isCanceled: e.target.checked })}
                  />
                  Canceled
                </label>
                <label className="form-label">Request Limit</label>
                <input
                  type="number"
                  value={newEvent.requestLimit}
                  onChange={(e) => setNewEvent({ ...newEvent, requestLimit: parseInt(e.target.value) || 5 })}
                  placeholder="e.g., 5"
                  className="form-input"
                  min="1"
                />
                <div className="modal-buttons">
                  <button 
                    className="action-button add-button" 
                    onClick={createEvent}
                    onTouchStart={createEvent}
                  >
                    Add Event
                  </button>
                  <button
                    className="action-button cancel-button"
                    onClick={() => setShowAddEventModal(false)}
                    onTouchStart={() => setShowAddEventModal(false)}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

        {editEvent && (
          <div className="modal-overlay mobile-event-management">
            <div className="modal-content edit-event-modal">
              <h2 className="modal-title">Edit Event</h2>
              <div className="add-event-form">
                <label className="form-label">Event Code</label>
                <input
                  type="text"
                  value={editEvent.eventCode}
                  onChange={(e) => setEditEvent({ ...editEvent, eventCode: e.target.value })}
                  placeholder="e.g., KARAOKE_20250707"
                  className="form-input"
                />
                <label className="form-label">Description</label>
                <input
                  type="text"
                  value={editEvent.description || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, description: e.target.value })}
                  placeholder="e.g., Karaoke Night"
                  className="form-input"
                />
                <label className="form-label">Location</label>
                <input
                  type="text"
                  value={editEvent.location || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, location: e.target.value })}
                  placeholder="e.g., Club XYZ"
                  className="form-input"
                />
                <label className="form-label">Scheduled Date</label>
                <input
                  type="date"
                  value={editEvent.scheduledDate || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledDate: e.target.value })}
                  className="form-input"
                />
                <label className="form-label">Start Time (e.g., 18:00 or 6:00 PM)</label>
                <input
                  type="text"
                  value={editEvent.scheduledStartTime || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledStartTime: e.target.value })}
                  placeholder="HH:mm or h:mm AM/PM"
                  className="form-input"
                />
                <label className="form-label">End Time (e.g., 23:00 or 11:00 PM)</label>
                <input
                  type="text"
                  value={editEvent.scheduledEndTime || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledEndTime: e.target.value })}
                  placeholder="HH:mm or h:mm AM/PM"
                  className="form-input"
                />
                <label className="form-label">Karaoke DJ Name</label>
                <input
                  type="text"
                  value={editEvent.karaokeDJName || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, karaokeDJName: e.target.value })}
                  placeholder="e.g., DJ Groove"
                  className="form-input"
                />
                <label className="form-checkbox">
                  <input
                    type="checkbox"
                    checked={editEvent.isCanceled || false}
                    onChange={(e) => setEditEvent({ ...editEvent, isCanceled: e.target.checked })}
                  />
                  Canceled
                </label>
                <label className="form-label">Request Limit</label>
                <input
                  type="number"
                  value={editEvent.requestLimit || 5}
                  onChange={(e) => setEditEvent({ ...editEvent, requestLimit: parseInt(e.target.value) || 5 })}
                  placeholder="e.g., 5"
                  className="form-input"
                  min="1"
                />
                <div className="modal-buttons">
                  <button 
                    className="action-button update-button" 
                    onClick={updateEvent}
                    onTouchStart={updateEvent}
                  >
                    Update
                  </button>
                  <button
                    className="action-button cancel-button"
                    onClick={() => setEditEvent(null)}
                    onTouchStart={() => setEditEvent(null)}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  } catch (error) {
    console.error("[EVENT_MANAGEMENT] Render error:", error);
    return <div>Error in EventManagementPage: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default EventManagementPage;