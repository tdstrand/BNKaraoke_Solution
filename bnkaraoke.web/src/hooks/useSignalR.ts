// src/hooks/useSignalR.ts
import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, HubConnection, LogLevel, HttpTransportType, HttpClient, HttpRequest, HttpResponse } from '@microsoft/signalr';
import { EventQueueItem, Song, Event } from '../types';
import API_BASE_URL, { API_ROUTES } from '../config/apiConfig';

interface EventQueueDto {
  queueId: number;
  eventId: number;
  songId: number;
  songTitle: string;
  songArtist: string;
  youTubeUrl?: string;
  requestorUserName: string;
  requestorFullName: string | null;
  singers: string[];
  position: number;
  status: string;
  isActive: boolean;
  wasSkipped: boolean;
  isCurrentlyPlaying: boolean;
  sungAt?: string;
  isOnBreak: boolean;
  isServerCached: boolean;
}

interface UseSignalRProps {
  currentEvent: { eventId: number; status: string } | null;
  isCurrentEventLive: boolean;
  checkedIn: boolean;
  navigate: (path: string) => void;
  setGlobalQueue: React.Dispatch<React.SetStateAction<EventQueueItem[]>>;
  setMyQueues: React.Dispatch<React.SetStateAction<{ [eventId: number]: EventQueueItem[] }>>;
  setSongDetailsMap: React.Dispatch<React.SetStateAction<{ [songId: number]: Song }>>;
  setIsOnBreak: React.Dispatch<React.SetStateAction<boolean>>;
}

interface SignalRReturn {
  signalRError: string | null;
  serverAvailable: boolean;
  queuesLoading: boolean;
}

const WS_BASE_URL = API_BASE_URL.replace(/^http/, 'ws');
const NEGOTIATE_URL = `${API_BASE_URL}/hubs/karaoke-dj/negotiate`;
const HEALTH_CHECK_URL = `${API_BASE_URL}/api/events/health`;

const useSignalR = ({
  currentEvent,
  isCurrentEventLive,
  checkedIn,
  navigate,
  setGlobalQueue,
  setMyQueues,
  setSongDetailsMap,
  setIsOnBreak,
}: UseSignalRProps): SignalRReturn => {
  const [signalRError, setSignalRError] = useState<string | null>(null);
  const [serverAvailable, setServerAvailable] = useState<boolean>(true);
  const [queuesLoading, setQueuesLoading] = useState<boolean>(false);
  const hubConnectionRef = useRef<HubConnection | null>(null);
  const transportRef = useRef<HttpTransportType[]>([
    HttpTransportType.WebSockets,
    HttpTransportType.ServerSentEvents,
    HttpTransportType.LongPolling,
  ]);
  const pollingIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const signalRDisabled = useRef<boolean>(false);
  const connectionAttemptsRef = useRef<number>(0);
  const maxConnectionAttempts = 3;
  const attendanceCheckTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  const validateToken = useCallback(() => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[SIGNALR] No token or userName found", { token: !!token, userName: !!userName });
      setSignalRError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      console.log("[SIGNALR] Token payload:", { iss: payload.iss, aud: payload.aud, exp: new Date(payload.exp * 1000).toISOString() });
      return token;
    } catch (err) {
      console.error("[SIGNALR] Token validation error:", err);
      setSignalRError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  }, [navigate]);

  const checkServerHealth = useCallback(async (): Promise<boolean> => {
    try {
      console.log("[SIGNALR] Checking server health at:", HEALTH_CHECK_URL);
      const response = await fetch(HEALTH_CHECK_URL, {
        method: 'GET',
      });
      console.log("[SIGNALR] Server health response:", { status: response.status });
      if (!response.ok) {
        throw new Error(`Server health check failed: ${response.status}`);
      }
      setServerAvailable(true);
      return true;
    } catch (err) {
      console.error("[SIGNALR] Server health check error:", err);
      setServerAvailable(false);
      setSignalRError("Unable to connect to the server. Please check if the server is running.");
      return false;
    }
  }, []);

  const checkAttendanceStatus = useCallback(async (eventId: number, token: string): Promise<boolean> => {
    try {
      console.log(`[SIGNALR] Checking attendance status for event ${eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${eventId}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[SIGNALR] Attendance status response:", { status: response.status, body: responseText });
      if (!response.ok) {
        if (response.status === 401) {
          setSignalRError("Session expired. Please log in again.");
          navigate("/login");
          return false;
        }
        throw new Error(`Attendance status check failed: ${response.status}`);
      }
      const data = JSON.parse(responseText);
      console.log("[SIGNALR] Attendance status:", data);
      return data.isCheckedIn || false;
    } catch (err) {
      console.error("[SIGNALR] Attendance status check error:", err);
      setSignalRError("Failed to verify attendance status.");
      return false;
    }
  }, [navigate]);

  const fetchPersonalQueue = useCallback(async (token: string, userName: string) => {
    try {
      console.log("[SIGNALR] Fetching personal queue from:", API_ROUTES.USER_REQUESTS);
      const response = await fetch(API_ROUTES.USER_REQUESTS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        if (response.status === 401) {
          setSignalRError("Session expired. Please log in again.");
          navigate("/login");
          return;
        }
        throw new Error(`Fetch personal queue failed: ${response.status}`);
      }
      const queueData: EventQueueItem[] = await response.json();
      console.log("[SIGNALR] Personal queue data:", queueData);
      if (!currentEvent) return;
      const userQueue = queueData.filter(item => item.eventId === currentEvent.eventId && item.requestorUserName === userName && item.sungAt == null && !item.wasSkipped);
      console.log("[SIGNALR] Setting myQueues:", { eventId: currentEvent.eventId, userQueue });
      setMyQueues(prev => ({
        ...prev,
        [currentEvent.eventId]: userQueue.sort((a, b) => (a.position || 0) - (b.position || 0)),
      }));
    } catch (err) {
      console.error("[SIGNALR] Fetch personal queue error:", err);
      setSignalRError("Failed to load personal queue.");
    }
  }, [currentEvent, navigate, setMyQueues]);

  const mapQueueDtoToItem = (dto: EventQueueDto): EventQueueItem => {
    console.log("[SIGNALR] Mapping DTO:", { requestorUserName: dto.requestorUserName, requestorFullName: dto.requestorFullName, singers: dto.singers, sungAt: dto.sungAt, status: dto.status });
    return {
      queueId: dto.queueId,
      eventId: dto.eventId,
      songId: dto.songId,
      requestorUserName: dto.requestorUserName,
      requestorFullName: dto.requestorFullName,
      singers: dto.singers,
      position: dto.position,
      isCurrentlyPlaying: dto.isCurrentlyPlaying,
      isUpNext: dto.status.toLowerCase() === 'upnext',
      sungAt: dto.sungAt || null,
      wasSkipped: dto.wasSkipped,
      status: dto.status,
      isActive: dto.isActive,
      isOnBreak: dto.isOnBreak,
      songTitle: dto.songTitle,
      songArtist: dto.songArtist,
      youTubeUrl: dto.youTubeUrl,
      isServerCached: dto.isServerCached,
    };
  };

  const processQueueData = useCallback((queueItems: EventQueueDto[], source: string) => {
    if (!currentEvent) {
      console.warn(`[SIGNALR] ${source} ignored: no current event`);
      return;
    }
    console.log(`[SIGNALR] ${source} received:`, { queueItems, eventId: currentEvent.eventId, itemCount: queueItems.length });
    if (!Array.isArray(queueItems)) {
      console.error(`[SIGNALR] ${source} invalid data:`, { queueItems, type: typeof queueItems });
      setSignalRError("Received invalid queue data from server.");
      return;
    }
    queueItems.forEach((item, index) => {
      console.log(`[SIGNALR] ${source} item ${index + 1}:`, {
        queueId: item.queueId,
        eventId: item.eventId,
        songId: item.songId,
        songTitle: item.songTitle,
        songArtist: item.songArtist,
        requestorUserName: item.requestorUserName,
        requestorFullName: item.requestorFullName,
        singers: item.singers,
        position: item.position,
        status: item.status,
        isActive: item.isActive,
        wasSkipped: item.wasSkipped,
        isCurrentlyPlaying: item.isCurrentlyPlaying,
        sungAt: item.sungAt,
        isOnBreak: item.isOnBreak,
      });
    });
    const filteredQueueItems = queueItems
      .filter(item => item.eventId === currentEvent.eventId && !item.wasSkipped)
      .map(mapQueueDtoToItem);
    console.log(`[SIGNALR] Filtered globalQueue items:`, filteredQueueItems);

    // Update globalQueue items with incoming data
    setGlobalQueue(prev => {
      const map = new Map(prev.map(item => [item.queueId, item]));
      filteredQueueItems.forEach(item => {
        const existing = map.get(item.queueId) || {};
        map.set(item.queueId, { ...existing, ...item });
      });
      const mergedQueue = Array.from(map.values())
        .filter(item => item.eventId === currentEvent.eventId && !item.wasSkipped)
        .sort((a, b) => (a.position || 0) - (b.position || 0));
      console.log(`[SIGNALR] Updated globalQueue:`, mergedQueue.map(item => ({ queueId: item.queueId, position: item.position })));
      return mergedQueue;
    });

    // Update myQueues items with incoming data
    const userName = localStorage.getItem("userName") || "";
    const userQueue = filteredQueueItems.filter(item => item.requestorUserName === userName && item.sungAt == null && !item.wasSkipped);
    setMyQueues(prev => {
      const existingUserQueue = prev[currentEvent.eventId] || [];
      const map = new Map(existingUserQueue.map(item => [item.queueId, item]));
      userQueue.forEach(item => {
        const existing = map.get(item.queueId) || {};
        map.set(item.queueId, { ...existing, ...item });
      });
      const mergedUserQueue = Array.from(map.values()).sort((a, b) => (a.position || 0) - (b.position || 0));
      const newMyQueues = { ...prev, [currentEvent.eventId]: mergedUserQueue };
      console.log(`[SIGNALR] Updated myQueues:`, { eventId: currentEvent.eventId, items: mergedUserQueue.map(item => ({ queueId: item.queueId, position: item.position })) });
      return newMyQueues;
    });

    queueItems.forEach(item => {
      setSongDetailsMap(prev => ({
        ...prev,
        [item.songId]: {
          id: item.songId,
          title: item.songTitle || `Song ${item.songId}`,
          artist: item.songArtist || 'Unknown',
          status: item.status || 'Active',
          requestDate: null,
          requestedBy: item.requestorUserName || null,
        },
      }));
    });
  }, [currentEvent, setGlobalQueue, setMyQueues, setSongDetailsMap]);

  const buildConnection = useCallback((token: string, transport: HttpTransportType) => {
    const hubUrl = currentEvent ? `${WS_BASE_URL}/hubs/karaoke-dj?eventId=${currentEvent.eventId}` : `${WS_BASE_URL}/hubs/karaoke-dj`;
    console.log(`[SIGNALR] Building connection to hub: ${hubUrl}, transport: ${HttpTransportType[transport]}`);
    return new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => {
          console.log("[SIGNALR] accessTokenFactory called, returning token:", token.slice(0, 10) + "...");
          return token;
        },
        transport: transport,
        timeout: 60000,
        httpClient: new class extends HttpClient {
          async send(request: HttpRequest): Promise<HttpResponse> {
            console.log("[SIGNALR] Custom HttpClient send:", { url: request.url, method: request.method, headers: request.headers });
            if (request.url?.includes("/negotiate")) {
              const negotiateUrl = `${NEGOTIATE_URL}?eventId=${currentEvent?.eventId || ''}&access_token=${token}`;
              console.log("[SIGNALR] Overriding negotiate URL to:", negotiateUrl);
              request.url = negotiateUrl;
            }
            if (!request.url) {
              throw new Error("Request URL is undefined");
            }
            const response = await fetch(request.url, {
              method: request.method || 'POST',
              headers: {
                ...request.headers,
                Authorization: `Bearer ${token}`,
              },
              body: request.content,
            });
            const content = await response.text();
            console.log("[SIGNALR] Custom HttpClient response:", { status: response.status, content });
            return new HttpResponse(response.status, response.statusText, content);
          }
        }(),
      })
      .withAutomaticReconnect([15000, 30000, 60000])
      .configureLogging(LogLevel.Error)
      .build();
  }, [currentEvent]);

  const setupConnection = useCallback((token: string, userName: string, transport: HttpTransportType) => {
    const connection = buildConnection(token, transport);
    hubConnectionRef.current = connection;
    connection.on("InitialQueue", (queueItems: EventQueueDto[]) => {
      processQueueData(queueItems, "InitialQueue");
      setQueuesLoading(false);
    });
    connection.on("QueueUpdated", (message: { data: EventQueueDto | EventQueueDto[] | number; action: string }) => {
      const { data, action } = message;
      console.log("[SIGNALR] QueueUpdated received:", { data, action, eventId: currentEvent?.eventId });
      let queueItems: EventQueueDto[];
      if (!Array.isArray(data)) {
        // Handle single update object by fetching the full queue or constructing a minimal item
        console.warn("[SIGNALR] QueueUpdated received single object, fetching full queue for merge");
        if (typeof data === "number") {
          // Construct a minimal placeholder when only queueId is provided
          queueItems = [{
            queueId: data,
            eventId: currentEvent?.eventId ?? 0,
            songId: 0,
            songTitle: "",
            songArtist: "",
            requestorUserName: "",
            requestorFullName: null,
            singers: [],
            position: 0,
            status: "",
            isActive: false,
            wasSkipped: false,
            isCurrentlyPlaying: false,
            isOnBreak: false,
            isServerCached: false,
          }];
        } else {
          queueItems = [data];
        }
        // Note: For a proper fix, uncomment and implement the fetch below if API supports it
        // const token = validateToken();
        // if (token && currentEvent) {
        //   const response = await fetch(`${API_ROUTES.EVENT_QUEUE}/${currentEvent.eventId}/queue`, {
        //     headers: { Authorization: `Bearer ${token}` },
        //   });
        //   if (response.ok) {
        //     queueItems = await response.json();
        //   } else {
        //     console.error("[SIGNALR] Failed to fetch full queue:", response.status);
        //     queueItems = [data]; // Fallback to single item
        //   }
        // }
      } else {
        queueItems = data;
      }
      processQueueData(queueItems, `QueueUpdated (${action})`);
    });
    connection.on("EventUpdated", (eventDto: Event) => {
      console.log("[SIGNALR] EventUpdated received:", eventDto);
      if (currentEvent && eventDto.eventId === currentEvent.eventId) {
        setCurrentEvent(eventDto);
      }
    });
    connection.on("QueuePlaying", (queueId: number, eventId: number, youTubeUrl?: string) => {
      if (eventId !== currentEvent?.eventId) return;
      console.log("[SIGNALR] QueuePlaying received:", { queueId, eventId, youTubeUrl });
      setGlobalQueue((prev) => {
        const updated = prev.map(item =>
          item.queueId === queueId
            ? { ...item, isCurrentlyPlaying: true, status: "Playing" }
            : { ...item, isCurrentlyPlaying: false }
        );
        return updated.sort((a, b) => (a.position || 0) - (b.position || 0));
      });
      setMyQueues(prev => {
        const userQueue = prev[currentEvent.eventId] || [];
        const updatedUserQueue = userQueue.map(item =>
          item.queueId === queueId
            ? { ...item, isCurrentlyPlaying: true, status: "Playing" }
            : { ...item, isCurrentlyPlaying: false }
        );
        return {
          ...prev,
          [currentEvent.eventId]: updatedUserQueue.sort((a, b) => (a.position || 0) - (b.position || 0)),
        };
      });
    });
    connection.on("Connected", (connectionId: string) => {
      console.log("[SIGNALR] Connected to SignalR:", { connectionId });
    });
    connection.onclose((err?: Error) => {
      if (err) {
        console.error("[SIGNALR] Connection closed with error:", err);
        setSignalRError("Lost real-time queue updates. Attempting to reconnect.");
        setServerAvailable(false);
      } else {
        console.log("[SIGNALR] Connection closed successfully");
        setSignalRError(null);
        setServerAvailable(false);
      }
    });
    return connection;
  }, [buildConnection, currentEvent, setMyQueues, setGlobalQueue, processQueueData]);

  const attemptConnection = useCallback(async () => {
    if (!currentEvent || !isCurrentEventLive || !checkedIn) {
      console.log("[SIGNALR] Skipping connection attempt:", {
        currentEvent: !!currentEvent,
        isCurrentEventLive,
        checkedIn,
      });
      if (currentEvent && !isCurrentEventLive && checkedIn) {
        const token = validateToken();
        if (token) {
          const userName = localStorage.getItem("userName") || "";
          fetchPersonalQueue(token, userName);
        }
      }
      return;
    }
    setQueuesLoading(true);
    if (signalRDisabled.current || connectionAttemptsRef.current >= maxConnectionAttempts) {
      console.log("[SIGNALR] SignalR disabled or max connection attempts reached:", { signalRDisabled: signalRDisabled.current, attempts: connectionAttemptsRef.current });
      setSignalRError("Failed to connect to real-time queue updates. Please refresh the page.");
      signalRDisabled.current = true;
      return;
    }
    const isServerHealthy = await checkServerHealth();
    if (!isServerHealthy) {
      console.error("[SIGNALR] Server health check failed, aborting connection attempt");
      return;
    }
    const token = validateToken();
    if (!token) return;
    const userName = localStorage.getItem("userName");
    if (!userName) {
      console.error("[SIGNALR] Username missing");
      setSignalRError("Username missing. Please log in again.");
      navigate("/login");
      return;
    }
    if (attendanceCheckTimeoutRef.current) {
      clearTimeout(attendanceCheckTimeoutRef.current);
    }
    attendanceCheckTimeoutRef.current = setTimeout(async () => {
      const isCheckedIn = await checkAttendanceStatus(currentEvent.eventId, token);
      if (!isCheckedIn) {
        console.error("[SIGNALR] User not checked in for event", currentEvent.eventId);
        setSignalRError("You are not checked in for this event.");
        return;
      }
      connectionAttemptsRef.current += 1;
      console.log("[SIGNALR] Connection attempt", connectionAttemptsRef.current, "of", maxConnectionAttempts);
      for (const transport of transportRef.current) {
        try {
          const connection = setupConnection(token, userName, transport);
          console.log("[SIGNALR] Starting connection with transport:", HttpTransportType[transport]);
          await connection.start();
          console.log("[SIGNALR] Connection established with transport:", HttpTransportType[transport]);
          connectionAttemptsRef.current = 0;
          setSignalRError(null);
          setServerAvailable(true);
          break;
        } catch (err) {
          console.error("[SIGNALR] Connection attempt", connectionAttemptsRef.current, "failed for transport", HttpTransportType[transport], ":", err);
          setSignalRError(`Failed to connect with ${HttpTransportType[transport]}. Retrying...`);
          if (connectionAttemptsRef.current >= maxConnectionAttempts) {
            console.error("[SIGNALR] Max retries reached, disabling SignalR");
            setSignalRError("Failed to connect to real-time queue updates. Please refresh the page.");
            signalRDisabled.current = true;
            setServerAvailable(false);
            break;
          }
        }
      }
    }, 10000);
  }, [checkAttendanceStatus, currentEvent, isCurrentEventLive, checkedIn, navigate, checkServerHealth, validateToken, setupConnection, fetchPersonalQueue]);

  useEffect(() => {
    if (!currentEvent || !isCurrentEventLive || !checkedIn) {
      console.log("[SIGNALR] Effect: Skipping connection setup", { currentEvent: !!currentEvent, isCurrentEventLive, checkedIn });
      if (hubConnectionRef.current && hubConnectionRef.current.state === HubConnectionState.Connected) {
        console.log("[SIGNALR] Stopping existing connection");
        hubConnectionRef.current.stop().then(() => {
          console.log("[SIGNALR] Connection stopped successfully during cleanup");
        }).catch(err => {
          console.error("[SIGNALR] Error stopping connection during cleanup:", err);
        });
      }
      setQueuesLoading(false);
      return;
    }
    attemptConnection();
    const pollingInterval = pollingIntervalRef.current;
    const attendanceCheckTimeout = attendanceCheckTimeoutRef.current;
    return () => {
      console.log("[SIGNALR] Cleanup: Stopping connection and clearing intervals");
      if (hubConnectionRef.current && hubConnectionRef.current.state !== HubConnectionState.Disconnected) {
        hubConnectionRef.current.stop().then(() => {
          console.log("[SIGNALR] Connection stopped successfully during cleanup");
        }).catch(err => {
          console.error("[SIGNALR] Error stopping connection during cleanup:", err);
        });
      }
      if (pollingInterval) {
        clearInterval(pollingInterval);
      }
      if (attendanceCheckTimeout) {
        clearTimeout(attendanceCheckTimeout);
      }
    };
  }, [currentEvent, isCurrentEventLive, checkedIn, attemptConnection]);

  return { signalRError, serverAvailable, queuesLoading };
};

export default useSignalR;