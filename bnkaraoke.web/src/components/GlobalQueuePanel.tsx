// src/components/GlobalQueuePanel.tsx
import React, { memo } from 'react';
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
  const userName = localStorage.getItem("userName") || "";
  const filteredGlobalQueue = globalQueue.filter(item => item.sungAt === null && item.wasSkipped === false);
  const songsSung = globalQueue.filter(item => item.status.toLowerCase() === "sung" || item.sungAt !== null).length;
  console.log("[GLOBAL_QUEUE] globalQueue:", globalQueue.map(item => ({
    queueId: item.queueId,
    requestorUserName: item.requestorUserName,
    requestorFullName: item.requestorFullName,
    sungAt: item.sungAt,
    status: item.status,
    wasSkipped: item.wasSkipped,
  })));
  console.log("[GLOBAL_QUEUE] filteredGlobalQueue:", filteredGlobalQueue.map(item => ({
    queueId: item.queueId,
    requestorUserName: item.requestorUserName,
    requestorFullName: item.requestorFullName,
  })));
  console.log("[GLOBAL_QUEUE] Songs Sung:", songsSung, { sungItems: globalQueue.filter(item => item.status.toLowerCase() === "sung" || item.sungAt !== null) });

  return (
    <>
      {checkedIn && isCurrentEventLive && currentEvent && (
        <aside className="global-queue-panel">
          <h2>Karaoke DJ Queue</h2>
          <h3>{currentEvent.description} (In Queue: {filteredGlobalQueue.length} -- Songs Sung: {songsSung})</h3>
          {filteredGlobalQueue.length === 0 ? (
            <p>No songs in the Karaoke DJ Queue.</p>
          ) : (
            <div className="event-queue">
              {filteredGlobalQueue.map((queueItem: EventQueueItem) => {
                console.log("[GLOBAL_QUEUE] Rendering item:", {
                  queueId: queueItem.queueId,
                  requestorUserName: queueItem.requestorUserName,
                  requestorFullName: queueItem.requestorFullName,
                  isCurrentlyPlaying: queueItem.isCurrentlyPlaying,
                  isUserSong: queueItem.requestorUserName === userName,
                });
                const song = songDetailsMap[queueItem.songId] || {
                  id: queueItem.songId,
                  title: queueItem.songTitle || `Song ${queueItem.songId}`,
                  artist: queueItem.songArtist || 'Unknown',
                  status: 'unknown',
                };
                return (
                  <div
                    key={queueItem.queueId}
                    className={`queue-song ${queueItem.isCurrentlyPlaying ? 'now-playing' : ''} ${queueItem.isUpNext ? 'up-next' : ''} ${queueItem.requestorUserName === userName ? 'user-song' : ''}`}
                    onClick={() => {
                      if (song) handleGlobalQueueItemClick(song);
                    }}
                    onTouchStart={() => {
                      if (song) handleGlobalQueueItemClick(song);
                    }}
                  >
                    <div>
                      <span>
                        {song.title} - {song.artist}
                        {queueItem.isCurrentlyPlaying ? ' (Now Playing)' : ''}
                      </span>
                      <br />
                      <span className={`queue-requestor ${queueItem.isCurrentlyPlaying ? 'now-playing' : ''}`}>
                        Requested by: {queueItem.requestorFullName || queueItem.requestorUserName || 'Unknown'}
                      </span>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </aside>
      )}
    </>
  );
};

export default memo(GlobalQueuePanel);