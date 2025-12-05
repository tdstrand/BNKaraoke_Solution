export interface QueueApiErrorPayload {
  message?: string;
  detail?: string;
  errorCode?: string;
}

export const QUEUE_ERROR_CODE_DUPLICATE = "DuplicateQueueEntry";
export const DEFAULT_DUPLICATE_QUEUE_MESSAGE = "That song is already in the active queue for this event.";
export const GENERIC_QUEUE_ERROR_MESSAGE = "Failed to add song to queue. Please try again.";

export class QueueApiError extends Error {
  status: number;
  isDuplicate: boolean;
  payload: QueueApiErrorPayload | null;

  constructor(
    message: string,
    status: number,
    isDuplicate: boolean,
    payload: QueueApiErrorPayload | null = null
  ) {
    super(message);
    this.status = status;
    this.isDuplicate = isDuplicate;
    this.payload = payload;
    Object.setPrototypeOf(this, QueueApiError.prototype);
  }
}

export const parseQueueErrorPayload = (text: string): QueueApiErrorPayload | null => {
  if (!text) {
    return null;
  }
  try {
    const payload = JSON.parse(text);
    if (typeof payload !== "object" || payload === null) {
      return null;
    }
    return payload as QueueApiErrorPayload;
  } catch (err) {
    return null;
  }
};

export const createQueueApiError = (status: number, responseText: string): QueueApiError => {
  const payload = parseQueueErrorPayload(responseText);
  const normalizedText = responseText?.toLowerCase() ?? "";
  const isDuplicate =
    status === 409 ||
    (payload?.errorCode === QUEUE_ERROR_CODE_DUPLICATE) ||
    normalizedText.includes("already active");
  const defaultMessage = isDuplicate ? DEFAULT_DUPLICATE_QUEUE_MESSAGE : GENERIC_QUEUE_ERROR_MESSAGE;
  const message = payload?.message ?? payload?.detail ?? defaultMessage;
  return new QueueApiError(message, status, isDuplicate, payload);
};
