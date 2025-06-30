import React from 'react';
import { Event, EventQueueItem, Song } from '../types';

interface GlobalQueuePanelProps {
  currentEvent: Event | null;
  checkedIn: boolean;
  isCurrentEventLive: boolean;
  globalQueue: EventQueueItem[];
  myQueues: { [eventId: number]: EventQueueItem[] };
  songDetailsMap: { [songId: number]: Song };
  handleGlobalQueueItemClick: (song: Song) => void;
  enableDragAndDrop: boolean;
}

const GlobalQueuePanel: React.FC<GlobalQueuePanelProps> = ({
  currentEvent,
  checkedIn,
  isCurrentEventLive,
  globalQueue,
  myQueues,
  songDetailsMap,
  handleGlobalQueueItemClick,
  enableDragAndDrop: _enableDragAndDrop,
}) => {
  const filteredGlobalQueue = globalQueue.filter(item => item.sungAt == null && item.wasSkipped == false);

  return (
    <>
      {checkedIn && isCurrentEventLive && currentEvent && currentEvent.status.toLowerCase() === "live" && (
        <aside className="global-queue-panel">
          <h2>Karaoke DJ Queue</h2>
          <p className="queue-info">Total Songs: {filteredGlobalQueue.length}</p>
          {filteredGlobalQueue.length === 0 ? (
            <p>No songs in the Karaoke DJ Queue.</p>
          ) : (
            <div className="event-queue">
              <h3>{currentEvent.description}</h3>
              <p className="queue-info">{myQueues[currentEvent.eventId]?.filter(item => item.sungAt == null && item.wasSkipped == false).length || 0}/{currentEvent.requestLimit} songs</p>
              {filteredGlobalQueue.map((queueItem: EventQueueItem) => (
                <div
                  key={queueItem.queueId}
                  className={`queue-song ${queueItem.isCurrentlyPlaying ? 'now-playing' : ''} ${queueItem.isUpNext ? 'up-next' : ''}`}
                  onClick={() => {
                    const song = songDetailsMap[queueItem.songId];
                    if (song) handleGlobalQueueItemClick(song);
                  }}
                  onTouchStart={() => {
                    const song = songDetailsMap[queueItem.songId];
                    if (song) handleGlobalQueueItemClick(song);
                  }}
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

export default GlobalQueuePanel;