// src/components/QueuePanel.tsx
import React, { memo } from 'react';
import { DndContext, closestCenter, KeyboardSensor, PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { SortableItem } from './SortableItem';
import { Event, EventQueueItem, Song } from '../types';
import './QueuePanel.css';

interface QueuePanelProps {
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  myQueues: { [eventId: number]: EventQueueItem[] };
  songDetailsMap: { [songId: number]: Song };
  reorderError: string | null;
  showReorderErrorModal: boolean;
  handleQueueItemClick: (song: Song, queueId: number, eventId: number) => void;
  handleDragEnd: (event: any) => void;
  enableDragAndDrop: boolean;
}

const QueuePanel: React.FC<QueuePanelProps> = ({
  currentEvent,
  checkedIn,
  isCurrentEventLive,
  myQueues,
  songDetailsMap,
  reorderError,
  showReorderErrorModal,
  handleQueueItemClick,
  handleDragEnd,
  enableDragAndDrop,
}) => {
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const queueItems = currentEvent ? myQueues[currentEvent.eventId]?.filter(item => item.sungAt == null && item.wasSkipped == false) || [] : [];

  return (
    <div className="queue-panel">
      <h2>Your Queue</h2>
      <h3 className="queue-count">{queueItems.length} of {currentEvent?.requestLimit || 0} Allowed Requests</h3>
      {(!currentEvent || !checkedIn) && (
        <p className="info-text">
          {currentEvent ? "You are not checked in to the event." : "No event selected."}
        </p>
      )}
      {reorderError && showReorderErrorModal && <p className="error-text">{reorderError}</p>}
      {queueItems.length === 0 && currentEvent && checkedIn ? (
        <p className="info-text">Your queue is empty. Add a song to get started!</p>
      ) : (
        checkedIn && currentEvent && (
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={handleDragEnd}
          >
            <SortableContext
              items={queueItems.map(item => item.queueId.toString())}
              strategy={verticalListSortingStrategy}
              disabled={!enableDragAndDrop}
            >
              <div className="queue-list">
                {queueItems.map(item => {
                  console.log("[QUEUE_PANEL] Item:", {
                    queueId: item.queueId,
                    requestorUserName: item.requestorUserName,
                    requestorFullName: item.requestorFullName,
                  });
                  const song: Song = songDetailsMap[item.songId] || {
                    id: item.songId,
                    title: item.songTitle || `Song ${item.songId}`,
                    artist: item.songArtist || 'Unknown',
                    status: 'unknown',
                    bpm: 0,
                    danceability: 0,
                    energy: 0,
                    valence: undefined,
                    popularity: 0,
                    genre: undefined,
                    decade: undefined,
                    requestDate: '',
                    requestedBy: '',
                    spotifyId: undefined,
                    youTubeUrl: undefined,
                    approvedBy: undefined,
                    musicBrainzId: undefined,
                    mood: undefined,
                    lastFmPlaycount: undefined,
                  };
                  return (
                    <SortableItem
                      key={item.queueId}
                      id={item.queueId.toString()}
                      disabled={!enableDragAndDrop || item.isCurrentlyPlaying}
                      className={item.isCurrentlyPlaying ? 'now-playing' : ''}
                    >
                      <div
                        className="queue-item"
                        onClick={() => handleQueueItemClick(song, item.queueId, item.eventId)}
                      >
                        <span>{song.title} - {song.artist}</span>
                        {item.isCurrentlyPlaying && <span className="now-playing-label"> (Now Playing)</span>}
                      </div>
                    </SortableItem>
                  );
                })}
              </div>
            </SortableContext>
          </DndContext>
        )
      )}
    </div>
  );
};

export default memo(QueuePanel);