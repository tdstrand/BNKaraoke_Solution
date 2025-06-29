import React from 'react';
import { DndContext, closestCenter, KeyboardSensor, PointerSensor, useSensor, useSensors, DragEndEvent } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy, useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { Event, EventQueueItem, Song } from '../types';

interface SortableQueueItemProps {
  queueItem: EventQueueItem;
  eventId: number;
  songDetails: Song | null;
  onClick: (song: Song, queueId: number, eventId: number) => void;
}

const SortableQueueItem: React.FC<SortableQueueItemProps> = ({ queueItem, eventId, songDetails, onClick }) => {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: queueItem.queueId });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className={`queue-song ${queueItem.isCurrentlyPlaying ? 'now-playing' : ''} ${queueItem.isUpNext ? 'up-next' : ''}`}
      onClick={() => songDetails && onClick(songDetails, queueItem.queueId, eventId)}
      onTouchStart={() => songDetails && onClick(songDetails, queueItem.queueId, eventId)}
    >
      <span>
        {songDetails ? (
          `${queueItem.position}. ${songDetails.title} - ${songDetails.artist}`
        ) : (
          `Loading Song ${queueItem.songId}...`
        )}
      </span>
    </div>
  );
};

interface QueuePanelProps {
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  myQueues: { [eventId: number]: EventQueueItem[] };
  songDetailsMap: { [songId: number]: Song };
  reorderError: string | null;
  showReorderErrorModal: boolean;
  handleQueueItemClick: (song: Song, queueId: number, eventId: number) => void;
  handleDragEnd: (event: DragEndEvent) => void;
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

  return (
    <>
      {currentEvent !== null && (checkedIn || !isCurrentEventLive) && (
        <aside className="queue-panel">
          <h2>My Song Queue</h2>
          {reorderError && !showReorderErrorModal && <p className="error-text">{reorderError}</p>}
          {!currentEvent ? (
            <p>Please select an event to view your queue.</p>
          ) : !myQueues[currentEvent.eventId] || myQueues[currentEvent.eventId].length === 0 ? (
            <p>No songs in your queue for this event.</p>
          ) : enableDragAndDrop ? (
            <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
              <SortableContext items={myQueues[currentEvent.eventId].map(item => item.queueId)} strategy={verticalListSortingStrategy}>
                <div className="event-queue">
                  <h3>{currentEvent.description}</h3>
                  <p className="queue-info">{myQueues[currentEvent.eventId].filter(item => item.sungAt == null && item.wasSkipped == false).length}/{currentEvent.requestLimit} songs</p>
                  {myQueues[currentEvent.eventId]
                    .filter(item => item.sungAt == null && item.wasSkipped == false)
                    .map((queueItem: EventQueueItem) => (
                      <SortableQueueItem
                        key={queueItem.queueId}
                        queueItem={queueItem}
                        eventId={currentEvent.eventId}
                        songDetails={songDetailsMap[queueItem.songId] || null}
                        onClick={handleQueueItemClick}
                      />
                    ))}
                </div>
              </SortableContext>
            </DndContext>
          ) : (
            <div className="event-queue">
              <h3>{currentEvent.description}</h3>
              <p className="queue-info">{myQueues[currentEvent.eventId].filter(item => item.sungAt == null && item.wasSkipped == false).length}/{currentEvent.requestLimit} songs</p>
              {myQueues[currentEvent.eventId]
                .filter(item => item.sungAt == null && item.wasSkipped == false)
                .map((queueItem: EventQueueItem) => (
                  <div
                    key={queueItem.queueId}
                    className={`queue-song ${queueItem.isCurrentlyPlaying ? 'now-playing' : ''} ${queueItem.isUpNext ? 'up-next' : ''}`}
                    onClick={() => songDetailsMap[queueItem.songId] && handleQueueItemClick(songDetailsMap[queueItem.songId], queueItem.queueId, currentEvent.eventId)}
                    onTouchStart={() => songDetailsMap[queueItem.songId] && handleQueueItemClick(songDetailsMap[queueItem.songId], queueItem.queueId, currentEvent.eventId)}
                  >
                    <span>
                      {songDetailsMap[queueItem.songId] ? (
                        `${queueItem.position}. ${songDetailsMap[queueItem.songId].title} - ${songDetailsMap[queueItem.songId].artist}`
                      ) : (
                        `Loading Song ${queueItem.songId}...`
                      )}
                    </span>
                  </div>
                ))}
            </div>
          )}
        </aside>
      )}
    </>
  );
};

export default QueuePanel;