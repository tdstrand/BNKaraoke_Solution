import React from "react";
import { Event } from "../types";

type StatusOption = {
  label: string;
  value: string;
};

interface EventDetailsCardProps {
  event: Event;
  statusOptions: StatusOption[];
  formatDisplayDate: (value?: string) => string;
  formatDisplayTime: (value?: string | null) => string;
  onEdit: () => void;
  onDelete: () => void;
  onStart: () => void;
  onEnd: () => void;
  onToggleVisibility: () => void;
  onUpdateStatus: (status: string) => void;
  isStatusUpdating: boolean;
  isVisibilityUpdating: boolean;
  isDeleting: boolean;
}

const EventDetailsCard: React.FC<EventDetailsCardProps> = ({
  event,
  statusOptions,
  formatDisplayDate,
  formatDisplayTime,
  onEdit,
  onDelete,
  onStart,
  onEnd,
  onToggleVisibility,
  onUpdateStatus,
  isStatusUpdating,
  isVisibilityUpdating,
  isDeleting,
}) => {
  const isBusy = isStatusUpdating || isVisibilityUpdating || isDeleting;
  const visibilityLabel = event.visibility === "Visible" ? "Hide" : "Unhide";

  return (
    <article className="event-details-card">
      <header className="event-details-header">
        <div className="event-details-header-left">
          <div className="event-title-wrapper">
            <span className="event-index-badge">#{event.eventId}</span>
            <h2 className="event-title">{event.description}</h2>
          </div>
          <div className="event-meta-row event-details-meta">
            <span className="event-meta-chip">Code: {event.eventCode}</span>
            <span className="event-meta-chip">Location: {event.location || "No location"}</span>
            <span className="event-meta-chip">Date: {formatDisplayDate(event.scheduledDate)}</span>
            <span className="event-meta-chip">
              Start: {formatDisplayTime(event.scheduledStartTime)}
            </span>
            <span className="event-meta-chip">
              End: {formatDisplayTime(event.scheduledEndTime)}
            </span>
            <span className="event-meta-chip">Requests: {event.requestLimit}</span>
            <span className="event-meta-chip">Queue Items: {event.queueCount}</span>
          </div>
        </div>
        <div className="event-details-header-right">
          <span className={`status-pill status-${event.status.toLowerCase()}`}>{event.status}</span>
          <span className={`visibility-pill visibility-${event.visibility.toLowerCase()}`}>
            {event.visibility}
          </span>
        </div>
      </header>
      <div className="event-details-body">
        <div className="event-actions">
          <section className="event-action-section">
            <h3 className="event-section-title">Manage Details</h3>
            <div className="event-action-buttons">
              <button
                className="action-button edit-button"
                onClick={onEdit}
                disabled={event.status === "Archived" || isBusy}
              >
                Edit
              </button>
              <button
                className="action-button danger-button delete-button"
                onClick={onDelete}
                disabled={isBusy}
              >
                Delete
              </button>
            </div>
            <div className="section-footer">
              {isDeleting && <span className="event-action-note">Deleting event...</span>}
            </div>
          </section>

          <section className="event-action-section">
            <h3 className="event-section-title">Event Controls</h3>
            <div className="event-action-buttons">
              <button
                className="action-button start-button"
                onClick={onStart}
                disabled={event.status !== "Upcoming" || isBusy}
              >
                Start
              </button>
              <button
                className="action-button end-button"
                onClick={onEnd}
                disabled={event.status === "Archived" || isBusy}
              >
                End
              </button>
              <button
                className="action-button hide-button visibility-toggle-button"
                onClick={onToggleVisibility}
                disabled={isBusy}
              >
                {visibilityLabel}
              </button>
            </div>
            <div className="section-footer">
              {isVisibilityUpdating && (
                <span className="event-action-note">Updating visibility...</span>
              )}
            </div>
          </section>

          <section className="event-action-section">
            <h3 className="event-section-title">Status</h3>
            <div className="event-action-buttons status-buttons">
              {statusOptions.map((option) => (
                <button
                  key={option.value}
                  className={`action-button status-button${
                    event.status === option.value ? " active" : ""
                  }`}
                  onClick={() => onUpdateStatus(option.value)}
                  disabled={isBusy || event.status === option.value}
                >
                  {option.label}
                </button>
              ))}
            </div>
            <div className="section-footer">
              {isStatusUpdating && <span className="event-action-note">Updating status...</span>}
            </div>
          </section>
        </div>
      </div>
    </article>
  );
};

export default EventDetailsCard;
