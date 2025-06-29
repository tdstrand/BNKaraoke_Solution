import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, HubConnection, LogLevel, HttpTransportType, HttpClient, HttpRequest, HttpResponse } from '@microsoft/signalr';
import { Event, EventQueueItem, Song } from '../types';
import { API_ROUTES } from '../config/apiConfig';

interface UseSignalRProps {
  currentEvent: Event | null;
  isCurrentEventLive: boolean;
  checkedIn: boolean;
  navigate: (path: string) => void;
  setGlobalQueue: React.Dispatch<React.SetStateAction<EventQueueItem[]>>;
  setMyQueues: React.Dispatch<React.SetStateAction<{ [eventId: number]: EventQueueItem[] }>>;
  setSongDetailsMap: React.Dispatch<React.SetStateAction<{ [songId: number]: Song }>>;
  setHasAttemptedCheckIn: React.Dispatch<React.SetStateAction<boolean>>;
  setCheckedIn: React.Dispatch<React.SetStateAction<boolean>>;
  fetchQueue: () => Promise<void>;
}

interface SignalRReturn {
  signalRError: string | null;
  setSignalRError: React.Dispatch<React.SetStateAction<string | null>>;
  serverAvailable: boolean;
}

const API_BASE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com' : 'http://localhost:7290';
const WS_BASE_URL = process.env.NODE_ENV === 'production' ? 'wss://api.bnkaraoke.com' : 'ws://localhost:7290';
const NEGOTIATE_URL = process.env.NODE_ENV === 'production' ? 'https://api.bnkaraoke.com/hubs/karaoke-dj/negotiate' : 'http://localhost:7290/hubs/karaoke-dj/negotiate';

const useSignalR = ({
  currentEvent,
  isCurrentEventLive,
  checkedIn,
  navigate,
  setGlobalQueue,
  setMyQueues,
  setSongDetailsMap,
  setHasAttemptedCheckIn,
  setCheckedIn,
  fetchQueue,
}: UseSignalRProps): SignalRReturn => {
  const [signalRError, setSignalRError] = useState<string | null>(null);
  const [serverAvailable, setServerAvailable] = useState<boolean>(true);
  const hubConnectionRef = useRef<HubConnection | null>(null);
  const transportRef = useRef<HttpTransportType[]>([
    HttpTransportType.WebSockets,
    HttpTransportType.ServerSentEvents,
    HttpTransportType.LongPolling,
  ]);
  const pollingIntervalRef = useRef<NodeJS.Timeout | null>(null);
  const signalRDisabled = useRef<boolean>(false);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[SIGNALR] No token or userName found", { token: !!token, userName: !!userName });
      setSignalRError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }
    console.log("[SIGNALR] Token validated:", { userName, token: token.slice(0, 10) + "..." });
    return token;
  };

  const checkServerHealth = async (): Promise<boolean> => {
    try {
      const token = validateToken();
      if (!token) {
        console.error("[SIGNALR] No valid token for server health check");
        throw new Error("No valid token");
      }
      console.log("[SIGNALR] Checking server health at:", `${API_BASE_URL}/api/events`);
      const response = await fetch(`${API_BASE_URL}/api/events`, {
        method: 'GET',
        headers: { Authorization: `Bearer ${token}` },
      });
      console.log("[SIGNALR] Server health response:", { status: response.status, statusText: response.statusText });
      if (!response.ok) {
        if (response.status === 401) {
          console.error("[SIGNALR] Unauthorized during server health check, redirecting to login");
          setSignalRError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return false;
        }
        throw new Error(`Server health check failed: ${response.status} - ${response.statusText}`);
      }
      setServerAvailable(true);
      return true;
    } catch (err) {
      console.error("[SIGNALR] Server health check error:", err);
      setServerAvailable(false);
      setSignalRError("Unable to connect to the server. Please check if the server is running or log in again.");
      return false;
    }
  };

  const checkAttendanceStatus = async (eventId: number, token: string): Promise<boolean> => {
    try {
      console.log(`[SIGNALR] Checking attendance status for event ${eventId}`);
      const response = await fetch(`${API_ROUTES.EVENTS}/${eventId}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[SIGNALR] Attendance status response:", { status: response.status, body: responseText });
      if (!response.ok) {
        if (response.status === 401) {
          console.error("[SIGNALR] Unauthorized during attendance check, redirecting to login");
          setSignalRError("Session expired. Please log in again.");
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return false;
        }
        throw new Error(`Attendance status check failed: ${response.status} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      console.log("[SIGNALR] Attendance status:", data);
      return data.isCheckedIn || false;
    } catch (err) {
      console.error("[SIGNALR] Attendance status check error:", err);
      setSignalRError("Failed to verify attendance status. Please check your connection and try again.");
      return false;
    }
  };

  const buildConnection = (token: string, transport: HttpTransportType) => {
    const hubUrl = `${WS_BASE_URL}/hubs/karaoke-dj`;
    console.log(`[SIGNALR] Building connection to hub: ${hubUrl}, transport: ${HttpTransportType[transport]}`);
    return new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        transport: transport,
        timeout: 60000,
        httpClient: new class extends HttpClient {
          async send(request: HttpRequest): Promise<HttpResponse> {
            console.log("[SIGNALR] Custom HttpClient send:", { url: request.url, method: request.method, headers: request.headers });
            if (request.url?.includes("/negotiate")) {
              request.url = NEGOTIATE_URL + (request.url.includes("?") ? request.url.substring(request.url.indexOf("?")) : "");
              console.log("[SIGNALR] Overriding negotiate URL to:", request.url);
            }
            const response = await fetch(request.url!, {
              method: request.method || 'POST',
              headers: request.headers,
              body: request.content,
            });
            const content = await response.text();
            console.log("[SIGNALR] Custom HttpClient response:", { status: response.status, statusText: response.statusText, content });
            return new HttpResponse(response.status, response.statusText, content);
          }
        }(),
      })
      .withAutomaticReconnect([0, 5000, 10000, 20000, 40000, 60000])
      .configureLogging(LogLevel.Information)
      .build();
  };

  const setupConnection = (token: string, userName: string, transport: HttpTransportType) => {
    const connection = buildConnection(token, transport);
    hubConnectionRef.current = connection;

    connection.on("SingerStatusUpdated", (userId: string, eventId: number, displayName: string, isLoggedIn: boolean, isJoined: boolean, isOnBreak: boolean) => {
      console.log("[SIGNALR] SingerStatusUpdated received:", { userId, eventId, displayName, isLoggedIn, isJoined, isOnBreak });
      if (eventId !== currentEvent?.eventId) return;
      if (userId === userName) {
        setHasAttemptedCheckIn(isJoined);
        setCheckedIn(isJoined);
      }
    });

    connection.on("QueueUpdated", (queueItems: EventQueueItem[]) => {
      if (!currentEvent) return;
      console.log("[SIGNALR] QueueUpdated received:", queueItems);
      if (!Array.isArray(queueItems)) {
        console.error("[SIGNALR] QueueUpdated received invalid data: not an array", { queueItems, type: typeof queueItems });
        setSignalRError("Received invalid queue data from server. Falling back to polling.");
        fetchQueue();
        return;
      }

      queueItems.forEach(item => {
        console.log(`QueueUpdated: queueId=${item.queueId}, position=${item.position}`);
      });

      // Update globalQueue with filtered items from SignalR
      const filteredQueueItems = queueItems.filter(item => item.sungAt == null && !item.wasSkipped);
      setGlobalQueue(filteredQueueItems.sort((a, b) => (a.position || 0) - (b.position || 0)));

      // Filter myQueues by requestorUserName
      const userQueue = filteredQueueItems.filter(item => item.requestorUserName === userName);
      setMyQueues(prev => ({
        ...prev,
        [currentEvent.eventId]: userQueue.sort((a, b) => (a.position || 0) - (b.position || 0)),
      }));

      // Fetch song details for new items
      queueItems.forEach(item => {
        setSongDetailsMap(prev => {
          if (!prev[item.songId]) {
            fetch(`${API_ROUTES.SONG_BY_ID}/${item.songId}`, {
              headers: { Authorization: `Bearer ${token}` },
            })
              .then(res => {
                if (!res.ok) throw new Error(`[FETCH_SONG] Failed to fetch song ${item.songId}: ${res.status}`);
                return res.json();
              })
              .then(songData => {
                setSongDetailsMap(prevMap => ({
                  ...prevMap,
                  [songData.id]: {
                    ...songData,
                    title: songData.title || `Song ${item.songId}`,
                    artist: songData.artist || 'Unknown',
                  },
                }));
              })
              .catch((err: Error) => console.error("[FETCH_SONG] Error:", err));
          }
          return prev;
        });
      });
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
        const userQueue = updated.filter(item => item.requestorUserName === userName && item.sungAt == null && !item.wasSkipped);
        setMyQueues(prev => ({
          ...prev,
          [currentEvent.eventId]: userQueue.sort((a, b) => (a.position || 0) - (b.position || 0)),
        }));
        return updated.sort((a, b) => (a.position || 0) - (b.position || 0));
      });
    });

    connection.onclose((err?: Error) => {
      console.error("[SIGNALR] Connection closed:", err);
      setSignalRError("Lost real-time updates. Attempting to reconnect or using polling fallback.");
      setServerAvailable(false);
    });

    return connection;
  };

  const attemptConnection = async () => {
    if (!currentEvent || !isCurrentEventLive || !checkedIn) {
      console.log("[SIGNALR] Skipping connection attempt: no current event, not live, or not checked in", {
        currentEvent: !!currentEvent,
        isCurrentEventLive,
        checkedIn,
      });
      return;
    }

    if (signalRDisabled.current) {
      console.log("[SIGNALR] SignalR disabled, using polling fallback");
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

    const isCheckedIn = await checkAttendanceStatus(currentEvent.eventId, token);
    if (!isCheckedIn) {
      console.error("[SIGNALR] User not checked in for event", currentEvent.eventId);
      setSignalRError("You are not checked in to the event. Please check in to enable real-time updates.");
      setCheckedIn(false);
      setHasAttemptedCheckIn(true);
      return;
    }

    // Add 2-second delay to ensure server is ready
    console.log("[SIGNALR] Adding 2-second delay before connection attempt");
    await new Promise(resolve => setTimeout(resolve, 2000));

    let retryCount = 0;
    let transportIndex = 0;
    const maxRetries = 5;

    while (retryCount < maxRetries && transportIndex < transportRef.current.length) {
      const transport = transportRef.current[transportIndex];
      let connection = setupConnection(token, userName, transport);

      try {
        console.log(`[SIGNALR] Attempting negotiation, attempt ${retryCount + 1}/${maxRetries}, transport: ${HttpTransportType[transport]}, hub: ${NEGOTIATE_URL}`);
        const negotiateResponse = await fetch(`${NEGOTIATE_URL}?access_token=${token}`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
        });
        const negotiateText = await negotiateResponse.text();
        const headers = Object.fromEntries(negotiateResponse.headers.entries());
        console.log("[SIGNALR] Negotiate Response:", { status: negotiateResponse.status, statusText: negotiateResponse.statusText, body: negotiateText, headers });
        if (!negotiateResponse.ok) {
          console.error("[SIGNALR] Negotiation failed:", { status: negotiateResponse.status, statusText: negotiateResponse.statusText, body: negotiateText, headers });
          if (negotiateResponse.status === 401) {
            setSignalRError("Invalid authentication token. Please log in again.");
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            throw new Error("Unauthorized");
          }
          if (negotiateResponse.status === 404) {
            console.warn("[SIGNALR] Negotiation endpoint not found, falling back to health check");
            const healthResponse = await fetch(`${API_BASE_URL}/api/events`, {
              method: 'GET',
              headers: { Authorization: `Bearer ${token}` },
            });
            console.log("[SIGNALR] Fallback health check response:", { status: healthResponse.status, statusText: healthResponse.statusText });
            if (!healthResponse.ok) {
              throw new Error(`Fallback health check failed: ${healthResponse.status} - ${healthResponse.statusText}`);
            }
            throw new Error(`Negotiation failed: ${negotiateResponse.status} - ${negotiateResponse.statusText} - ${negotiateText}`);
          }
          throw new Error(`Negotiation failed: ${negotiateResponse.status} - ${negotiateResponse.statusText} - ${negotiateText}`);
        }
        let negotiateData;
        try {
          negotiateData = JSON.parse(negotiateText);
          console.log("[SIGNALR] Parsed negotiate response:", negotiateData);
        } catch (jsonError) {
          console.error("[SIGNALR] JSON parse error for negotiate response:", jsonError, "Raw response:", negotiateText);
          throw new Error("Invalid negotiate response format");
        }

        console.log(`[SIGNALR] Starting connection with transport ${HttpTransportType[transport]}, attempt ${retryCount + 1}/${maxRetries}`);
        await connection.start();
        console.log("[SIGNALR] Connected, ConnectionId:", connection.connectionId);
        await connection.invoke("JoinEventGroup", currentEvent.eventId);
        console.log(`[SIGNALR] Joined group Event_${currentEvent.eventId}`);
        setSignalRError(null);
        setServerAvailable(true);
        if (pollingIntervalRef.current) {
          clearInterval(pollingIntervalRef.current);
          pollingIntervalRef.current = null;
          console.log("[SIGNALR] Cleared polling interval on successful connection");
        }
        break;
      } catch (err: unknown) {
        retryCount++;
        console.error(`[SIGNALR] Connection attempt ${retryCount}/${maxRetries} failed for transport ${HttpTransportType[transport]}:`, err);
        const errorMessage = err instanceof Error ? err.message : "Unknown error";
        if (errorMessage.includes("ERR_CONNECTION_REFUSED")) {
          console.warn("[SIGNALR] Server not responding at", NEGOTIATE_URL);
          setSignalRError("Unable to connect to the server. Please check if the server is running and try again.");
          setServerAvailable(false);
        } else if (errorMessage.includes("Insufficient resources")) {
          console.warn("[SIGNALR] Insufficient resources detected, increasing retry delay");
          setSignalRError("Server resource issue detected. Retrying connection...");
          setServerAvailable(false);
        } else if (errorMessage.includes("Unauthorized")) {
          break; // Already handled
        } else if (errorMessage.includes("Negotiation failed") || errorMessage.includes("Invalid negotiate response format")) {
          console.error("[SIGNALR] Negotiation failure, checking server availability");
          setSignalRError(`Failed to negotiate with server: ${errorMessage}. Please check if the server is running.`);
          setServerAvailable(false);
        } else {
          setSignalRError(`Failed to connect using ${HttpTransportType[transport]}. Retrying... Error: ${errorMessage}`);
        }

        if (retryCount >= maxRetries && transportIndex < transportRef.current.length - 1) {
          transportIndex++;
          retryCount = 0;
          console.warn(`[SIGNALR] Switching to transport: ${HttpTransportType[transportRef.current[transportIndex]]}`);
          connection.stop().catch((stopErr: Error) => console.error("[SIGNALR] Stop error during transport switch:", stopErr));
          connection = setupConnection(token, userName, transportRef.current[transportIndex]);
        } else if (retryCount < maxRetries) {
          const delay = [5000, 10000, 20000, 40000, 60000][retryCount - 1] || 60000;
          console.log(`[SIGNALR] Retrying connection in ${delay}ms with transport ${HttpTransportType[transport]}...`);
          await new Promise(resolve => setTimeout(resolve, delay));
        } else if (transportIndex >= transportRef.current.length - 1) {
          console.error("[SIGNALR] Max retries reached for all transports, disabling SignalR");
          setSignalRError("Failed to connect to real-time updates after multiple attempts. Using polling fallback.");
          setServerAvailable(false);
          signalRDisabled.current = true;
        }
      }
    }
  };

  const connectToHub = useCallback(async () => {
    if (!currentEvent || !isCurrentEventLive || !checkedIn) {
      console.log("[SIGNALR] Skipping hub connection: no current event, not live, or not checked in", {
        currentEvent: !!currentEvent,
        isCurrentEventLive,
        checkedIn,
      });
      return;
    }

    await attemptConnection();
  }, [currentEvent, isCurrentEventLive, checkedIn]);

  useEffect(() => {
    connectToHub();
    return () => {
      if (hubConnectionRef.current?.state === HubConnectionState.Connected) {
        hubConnectionRef.current.stop().catch((err: Error) => console.error("[SIGNALR] Stop error:", err));
      }
      if (pollingIntervalRef.current) {
        clearInterval(pollingIntervalRef.current);
        pollingIntervalRef.current = null;
        console.log("[SIGNALR] Cleared polling interval on cleanup");
      }
      hubConnectionRef.current = null;
    };
  }, [connectToHub]);

  // Fallback polling when SignalR is disconnected or disabled
  useEffect(() => {
    if (signalRDisabled.current || !hubConnectionRef.current || hubConnectionRef.current.state !== HubConnectionState.Connected) {
      if (currentEvent && isCurrentEventLive && checkedIn && serverAvailable) {
        if (pollingIntervalRef.current) {
          clearInterval(pollingIntervalRef.current);
          console.log("[SIGNALR] Cleared existing polling interval before starting new one");
        }
        console.log("[SIGNALR] Starting fallback polling for queue updates");
        pollingIntervalRef.current = setInterval(async () => {
          const isHealthy = await checkServerHealth();
          if (isHealthy) {
            fetchQueue();
          } else {
            console.log("[SIGNALR] Skipping polling due to server unavailability");
          }
        }, 30000);
        return () => {
          if (pollingIntervalRef.current) {
            clearInterval(pollingIntervalRef.current);
            pollingIntervalRef.current = null;
            console.log("[SIGNALR] Cleared polling interval on cleanup");
          }
        };
      }
    } else if (pollingIntervalRef.current) {
      clearInterval(pollingIntervalRef.current);
      pollingIntervalRef.current = null;
      console.log("[SIGNALR] Cleared polling interval due to active SignalR connection");
    }
  }, [currentEvent, isCurrentEventLive, checkedIn, serverAvailable, fetchQueue]);

  return { signalRError, setSignalRError, serverAvailable };
};

export default useSignalR;