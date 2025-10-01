import React, { useEffect, useMemo, useRef, useState } from "react";

export type EventSummary = {
  eventId: string;
  name: string;
  eventIndex: number;
  status: string;
};

const STATUS_ORDER: Record<string, number> = {
  Live: 0,
  Upcoming: 1,
  Archived: 2,
};

const formatLabel = (event: EventSummary): string =>
  `${event.name} — #${event.eventIndex} (${event.status})`;

interface EventComboboxProps {
  events: EventSummary[];
  selectedId?: string;
  onSelect: (id: string) => void;
}

const EventCombobox: React.FC<EventComboboxProps> = ({ events, selectedId, onSelect }) => {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState<number>(-1);
  const containerRef = useRef<HTMLDivElement | null>(null);

  const sortedEvents = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    const filtered = normalizedQuery
      ? events.filter((event) => formatLabel(event).toLowerCase().includes(normalizedQuery))
      : events;

    return filtered
      .slice()
      .sort((a, b) => {
        const statusDelta = (STATUS_ORDER[a.status] ?? 3) - (STATUS_ORDER[b.status] ?? 3);
        if (statusDelta !== 0) return statusDelta;
        const nameDelta = a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
        if (nameDelta !== 0) return nameDelta;
        return a.eventIndex - b.eventIndex;
      });
  }, [events, query]);

  const selected = useMemo(
    () => events.find((event) => event.eventId === selectedId),
    [events, selectedId]
  );

  useEffect(() => {
    if (!open) {
      setQuery("");
      setActiveIndex(-1);
    }
  }, [open]);

  useEffect(() => {
    if (!open) return;
    if (sortedEvents.length === 0) {
      setActiveIndex(-1);
      return;
    }
    const matchedIndex = sortedEvents.findIndex((event) => event.eventId === selectedId);
    setActiveIndex(matchedIndex >= 0 ? matchedIndex : 0);
  }, [open, selectedId, sortedEvents]);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (!containerRef.current) return;
      if (!containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    };

    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const handleSelect = (event: EventSummary) => {
    onSelect(event.eventId);
    setOpen(false);
  };

  const handleInputFocus = () => {
    if (events.length === 0) return;
    setOpen(true);
    setQuery(selected ? formatLabel(selected) : "");
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Escape") {
      setOpen(false);
      (event.currentTarget as HTMLInputElement).blur();
      return;
    }

    if (events.length === 0) {
      return;
    }

    if (!open && (event.key === "ArrowDown" || event.key === "Enter")) {
      event.preventDefault();
      setOpen(true);
      return;
    }

    if (!open) return;

    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActiveIndex((prev) => {
        const next = prev + 1;
        return next >= sortedEvents.length ? 0 : next;
      });
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      setActiveIndex((prev) => {
        if (sortedEvents.length === 0) return -1;
        const next = prev - 1;
        return next < 0 ? sortedEvents.length - 1 : next;
      });
      return;
    }

    if (event.key === "Enter" && activeIndex >= 0 && sortedEvents[activeIndex]) {
      event.preventDefault();
      handleSelect(sortedEvents[activeIndex]);
    }
  };

  return (
    <div
      ref={containerRef}
      className={`em-combobox${events.length === 0 ? " em-combobox-disabled" : ""}`}
      role="combobox"
      aria-expanded={open}
      aria-controls="em-combobox-list"
      aria-haspopup="listbox"
    >
      <input
        className="em-combobox-input"
        type="text"
        placeholder={events.length === 0 ? "No events available" : "Search events…"}
        value={open ? query : selected ? formatLabel(selected) : ""}
        onFocus={handleInputFocus}
        onChange={(event) => {
          setOpen(true);
          setQuery(event.target.value);
        }}
        onKeyDown={handleKeyDown}
        aria-controls="em-combobox-list"
        aria-autocomplete="list"
        disabled={events.length === 0}
      />
      {open && events.length > 0 && (
        <div className="em-combobox-list" role="listbox" id="em-combobox-list">
          {sortedEvents.length === 0 ? (
            <div className="em-combobox-item" aria-disabled="true">
              No matching events
            </div>
          ) : (
            sortedEvents.map((event, index) => {
              const isSelected = event.eventId === selectedId;
              const isActive = index === activeIndex;
              return (
                <div
                  key={event.eventId}
                  role="option"
                  aria-selected={isSelected}
                  className={`em-combobox-item${isActive ? " em-combobox-item-active" : ""}`}
                  onMouseDown={(mouseEvent) => {
                    mouseEvent.preventDefault();
                    handleSelect(event);
                  }}
                >
                  {formatLabel(event)}
                </div>
              );
            })
          )}
        </div>
      )}
    </div>
  );
};

export default EventCombobox;
