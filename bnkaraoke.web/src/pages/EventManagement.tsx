// src/pages/EventManagement.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./EventManagement.css";
import { Event } from "../types";

interface EventUpdate {
  eventId: number; // Added to fix TypeScript error
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

const EventManagementPage: React.FC = () => {
  const navigate = useNavigate();
  const [events, setEvents] = useState<Event[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newEvent, setNewEvent] = useState({
    eventCode: "",
    description: "",
    status: "Upcoming",
    visibility: "Visible",
    location: "",
    scheduledDate: "",
    scheduledStartTime: "",
    scheduledEndTime: "",
    karaokeDJName: "",
    isCanceled: false,
    requestLimit: 5,
  });
  const [editEvent, setEditEvent] = useState<EventUpdate | null>(null);

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

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const storedRoles = localStorage.getItem("roles");
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("Event Manager")) {
        console.error("[EVENT_MANAGEMENT] User lacks Event Manager role, redirecting to dashboard");
        setError("You do not have permission to access event management. Event Manager role required.");
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
  }, [navigate]);

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

    if (!newEvent.eventCode) {
      setError("Please enter an event code");
      return;
    }
    try {
      console.log("[EVENT_MANAGEMENT] Creating event:", newEvent);
      const response = await fetch(API_ROUTES.CREATE_EVENT, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(newEvent),
      });
      const responseText = await response.text();
      console.log("[EVENT_MANAGEMENT] Create Event Raw Response:", responseText);
      if (!response.ok) {
        const errorMessage = response.status === 403
          ? "Unable to create event due to authorization error. Please contact support."
          : `Failed to create event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      alert("Event created successfully!");
      setNewEvent({
        eventCode: "",
        description: "",
        status: "Upcoming",
        visibility: "Visible",
        location: "",
        scheduledDate: "",
        scheduledStartTime: "",
        scheduledEndTime: "",
        karaokeDJName: "",
        isCanceled: false,
        requestLimit: 5,
      });
      fetchManageEvents(token);
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

    try {
      console.log(`[EVENT_MANAGEMENT] Updating event: ${editEvent.eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${editEvent.eventId}/update`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(editEvent),
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
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to update event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] Update Event Error:", err);
    }
  };

  const startEvent = async (eventId: number) => {
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
          : `Failed to start event: ${response.status} ${response.statusText}`;
        throw new Error(errorMessage);
      }
      alert("Event started successfully!");
      fetchManageEvents(token);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to start event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] Start Event Error:", err);
    }
  };

  const endEvent = async (eventId: number) => {
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
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Failed to end event. Please try again.";
      setError(errorMessage);
      console.error("[EVENT_MANAGEMENT] End Event Error:", err);
    }
  };

  try {
    return (
      <div className="event-management-container">
        <header className="event-management-header">
          <h1 className="event-management-title">Event Management</h1>
          <div className="header-buttons">
            <button className="action-button back-button" onClick={() => navigate("/dashboard")}>
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
                {events.map((event) => (
                  <li key={event.eventId} className="event-item">
                    <div className="event-info">
                      <p className="event-title">{event.description} ({event.eventCode})</p>
                      <p className="event-text">Status: {event.status} | Location: {event.location}</p>
                    </div>
                    <div className="event-actions">
                      <button
                        className="action-button edit-button"
                        onClick={() => setEditEvent({ ...event, eventId: event.eventId, eventCode: event.eventCode })}
                      >
                        Edit
                      </button>
                      <button
                        className="action-button start-button"
                        onClick={() => startEvent(event.eventId)}
                        disabled={event.status === "Live"}
                      >
                        Start
                      </button>
                      <button
                        className="action-button end-button"
                        onClick={() => endEvent(event.eventId)}
                        disabled={event.status === "Archived"}
                      >
                        End
                      </button>
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="event-management-text">{error ? "Failed to load events. Please try again or contact support." : "No events found."}</p>
            )}
          </section>
          <section className="event-management-card add-event-card">
            <h2 className="section-title">Add New Event</h2>
            <div className="add-event-form">
              <label className="form-label">Event Code</label>
              <input
                type="text"
                value={newEvent.eventCode}
                onChange={(e) => setNewEvent({ ...newEvent, eventCode: e.target.value })}
                placeholder="Event Code"
                className="form-input"
              />
              <label className="form-label">Description</label>
              <input
                type="text"
                value={newEvent.description}
                onChange={(e) => setNewEvent({ ...newEvent, description: e.target.value })}
                placeholder="Description"
                className="form-input"
              />
              <label className="form-label">Location</label>
              <input
                type="text"
                value={newEvent.location}
                onChange={(e) => setNewEvent({ ...newEvent, location: e.target.value })}
                placeholder="Location"
                className="form-input"
              />
              <label className="form-label">Scheduled Date</label>
              <input
                type="date"
                value={newEvent.scheduledDate}
                onChange={(e) => setNewEvent({ ...newEvent, scheduledDate: e.target.value })}
                className="form-input"
              />
              <label className="form-label">Start Time</label>
              <input
                type="time"
                value={newEvent.scheduledStartTime}
                onChange={(e) => setNewEvent({ ...newEvent, scheduledStartTime: e.target.value })}
                className="form-input"
              />
              <label className="form-label">End Time</label>
              <input
                type="time"
                value={newEvent.scheduledEndTime}
                onChange={(e) => setNewEvent({ ...newEvent, scheduledEndTime: e.target.value })}
                className="form-input"
              />
              <label className="form-label">Karaoke DJ Name</label>
              <input
                type="text"
                value={newEvent.karaokeDJName}
                onChange={(e) => setNewEvent({ ...newEvent, karaokeDJName: e.target.value })}
                placeholder="Karaoke DJ Name"
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
                placeholder="Request Limit"
                className="form-input"
              />
              <button className="action-button add-button" onClick={createEvent}>
                Add Event
              </button>
            </div>
          </section>
        </div>

        {editEvent && (
          <div className="modal-overlay">
            <div className="modal-content edit-event-modal">
              <h2 className="modal-title">Edit Event</h2>
              <div className="add-event-form">
                <label className="form-label">Event Code</label>
                <input
                  type="text"
                  value={editEvent.eventCode}
                  onChange={(e) => setEditEvent({ ...editEvent, eventCode: e.target.value })}
                  placeholder="Event Code"
                  className="form-input"
                />
                <label className="form-label">Description</label>
                <input
                  type="text"
                  value={editEvent.description || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, description: e.target.value })}
                  placeholder="Description"
                  className="form-input"
                />
                <label className="form-label">Location</label>
                <input
                  type="text"
                  value={editEvent.location || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, location: e.target.value })}
                  placeholder="Location"
                  className="form-input"
                />
                <label className="form-label">Scheduled Date</label>
                <input
                  type="date"
                  value={editEvent.scheduledDate || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledDate: e.target.value })}
                  className="form-input"
                />
                <label className="form-label">Start Time</label>
                <input
                  type="time"
                  value={editEvent.scheduledStartTime || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledStartTime: e.target.value })}
                  className="form-input"
                />
                <label className="form-label">End Time</label>
                <input
                  type="time"
                  value={editEvent.scheduledEndTime || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, scheduledEndTime: e.target.value })}
                  className="form-input"
                />
                <label className="form-label">Karaoke DJ Name</label>
                <input
                  type="text"
                  value={editEvent.karaokeDJName || ""}
                  onChange={(e) => setEditEvent({ ...editEvent, karaokeDJName: e.target.value })}
                  placeholder="Karaoke DJ Name"
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
                  placeholder="Request Limit"
                  className="form-input"
                />
                <div className="modal-buttons">
                  <button className="action-button update-button" onClick={updateEvent}>
                    Update
                  </button>
                  <button
                    className="action-button cancel-button"
                    onClick={() => setEditEvent(null)}
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