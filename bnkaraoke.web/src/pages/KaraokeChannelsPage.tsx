import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import { DndContext, closestCenter, KeyboardSensor, PointerSensor, useSensor, useSensors, DragEndEvent } from "@dnd-kit/core";
import { arrayMove, SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy, useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import "./KaraokeChannelsPage.css";

interface KaraokeChannel {
  id: number;
  channelName: string;
  channelId: string | null;
  sortOrder: number;
  isActive: boolean;
}

const KaraokeChannelsPage: React.FC = () => {
  const navigate = useNavigate();
  const [channels, setChannels] = useState<KaraokeChannel[]>([]);
  const [newChannel, setNewChannel] = useState({ channelName: "", channelId: "", isActive: true });
  const [editChannel, setEditChannel] = useState<KaraokeChannel | null>(null);
  const [error, setError] = useState<string | null>(null);

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  useEffect(() => {
    const token = localStorage.getItem("token");
    const storedRoles = localStorage.getItem("roles");
    if (!token) {
      console.log("No token found, redirecting to login");
      navigate("/");
      return;
    }
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("Song Manager")) {
        console.log("User lacks Song Manager role, redirecting to dashboard");
        navigate("/dashboard");
        return;
      }
    }
    fetchChannels(token);
  }, [navigate]);

  const fetchChannels = async (token: string) => {
    try {
      console.log(`Fetching karaoke channels from: ${API_ROUTES.KARAOKE_CHANNELS}`);
      const response = await fetch(API_ROUTES.KARAOKE_CHANNELS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("Karaoke Channels Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch channels: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data: KaraokeChannel[] = JSON.parse(responseText);
      console.log("Parsed Karaoke Channels:", data);
      setChannels(data.sort((a, b) => a.sortOrder - b.sortOrder));
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      setChannels([]);
      console.error("Fetch Channels Error:", errorMessage, err);
    }
  };

  const handleAddChannel = async (token: string) => {
    if (!newChannel.channelName.trim()) {
      setError("Channel name is required");
      return;
    }
    try {
      console.log(`Adding channel at: ${API_ROUTES.KARAOKE_CHANNELS}`);
      const response = await fetch(API_ROUTES.KARAOKE_CHANNELS, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          channelName: newChannel.channelName,
          channelId: newChannel.channelId || null,
          sortOrder: channels.length + 1,
          isActive: newChannel.isActive,
        }),
      });
      const responseText = await response.text();
      console.log("Add Channel Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to add channel: ${response.status} ${response.statusText} - ${responseText}`);
      }
      setNewChannel({ channelName: "", channelId: "", isActive: true });
      fetchChannels(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Add Channel Error:", errorMessage, err);
    }
  };

  const handleEditChannel = async (token: string) => {
    if (!editChannel || !editChannel.channelName.trim()) {
      setError("Channel name is required");
      return;
    }
    try {
      console.log(`Updating channel ${editChannel.id} at: ${API_ROUTES.KARAOKE_CHANNELS}/${editChannel.id}`);
      const response = await fetch(`${API_ROUTES.KARAOKE_CHANNELS}/${editChannel.id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          id: editChannel.id,
          channelName: editChannel.channelName,
          channelId: editChannel.channelId,
          sortOrder: editChannel.sortOrder,
          isActive: editChannel.isActive
        }),
      });
      if (!response.ok) {
        throw new Error(`Failed to update channel: ${response.status} ${response.statusText}`);
      }
      setEditChannel(null);
      fetchChannels(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Update Channel Error:", errorMessage, err);
    }
  };

  const handleDeleteChannel = async (id: number, token: string) => {
    try {
      console.log(`Deleting channel ${id} at: ${API_ROUTES.KARAOKE_CHANNELS}/${id}`);
      const response = await fetch(`${API_ROUTES.KARAOKE_CHANNELS}/${id}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to delete channel: ${response.status} ${response.statusText}`);
      }
      fetchChannels(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
      setError(errorMessage);
      console.error("Delete Channel Error:", errorMessage, err);
    }
  };

  const handleDragEnd = async (event: DragEndEvent, token: string) => {
    const { active, over } = event;
    if (over && active.id !== over.id) {
      const oldIndex = channels.findIndex((channel) => channel.id === active.id);
      const newIndex = channels.findIndex((channel) => channel.id === over.id);
      const newChannels = arrayMove(channels, oldIndex, newIndex).map((channel, index) => ({
        ...channel,
        sortOrder: index + 1,
      }));
      setChannels(newChannels);
      try {
        console.log(`Reordering channels at: ${API_ROUTES.KARAOKE_CHANNELS}/reorder`);
        const response = await fetch(`${API_ROUTES.KARAOKE_CHANNELS}/reorder`, {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${token}`,
          },
          body: JSON.stringify(newChannels.map(c => ({ Id: c.id, SortOrder: c.sortOrder }))),
        });
        if (!response.ok) {
          throw new Error(`Failed to reorder channels: ${response.status} ${response.statusText}`);
        }
        setError(null);
      } catch (err) {
        const errorMessage = err instanceof Error ? err.message : "Unknown fetch error";
        setError(errorMessage);
        console.error("Reorder Channels Error:", errorMessage, err);
      }
    }
  };

  const SortableChannelItem: React.FC<{ channel: KaraokeChannel }> = ({ channel }) => {
    const {
      attributes,
      listeners,
      setNodeRef,
      transform,
      transition,
    } = useSortable({ id: channel.id });

    const style = {
      transform: CSS.Transform.toString(transform),
      transition,
    };

    return (
      <li ref={setNodeRef} style={style} {...attributes} {...listeners} className="channel-item">
        <div className="channel-info">
          <p className="channel-title">{channel.channelName || "N/A"}</p>
          <p className="channel-text">
            Sort Order: {channel.sortOrder} | Active: {channel.isActive ? "YES" : "NO"}
          </p>
        </div>
        <div className="channel-actions">
          <button
            className="karaoke-channels-button edit-button"
            onClick={() => setEditChannel(channel)}
          >
            Edit
          </button>
          <button
            className="karaoke-channels-button delete-button"
            onClick={() => handleDeleteChannel(channel.id, token)}
          >
            Delete
          </button>
        </div>
      </li>
    );
  };

  const token = localStorage.getItem("token") || "";

  return (
    <div className="karaoke-channels-container">
      <header className="karaoke-channels-header">
        <h1 className="karaoke-channels-title">Manage Karaoke Channels</h1>
        <div className="header-buttons">
          <button className="karaoke-channels-button back-button" onClick={() => navigate("/song-manager")}>
            Back to Song Manager
          </button>
          <button className="karaoke-channels-button logout-button" onClick={() => { localStorage.clear(); navigate("/"); }}>
            Logout
          </button>
        </div>
      </header>

      <div className="karaoke-channels-content">
        <section className="karaoke-channels-card">
          <h2 className="section-title">Add New Channel</h2>
          {error && <p className="error-text">{error}</p>}
          <div className="add-channel-form">
            <input
              type="text"
              value={newChannel.channelName}
              onChange={(e) => setNewChannel({ ...newChannel, channelName: e.target.value })}
              placeholder="Channel Name"
              className="karaoke-channels-input"
            />
            <input
              type="text"
              value={newChannel.channelId}
              onChange={(e) => setNewChannel({ ...newChannel, channelId: e.target.value })}
              placeholder="Channel ID (optional)"
              className="karaoke-channels-input"
            />
            <label>
              <input
                type="checkbox"
                checked={newChannel.isActive}
                onChange={(e) => setNewChannel({ ...newChannel, isActive: e.target.checked })}
              />
              Active
            </label>
            <button
              className="karaoke-channels-button add-button"
              onClick={() => handleAddChannel(token)}
            >
              Add Channel
            </button>
          </div>
        </section>

        <section className="karaoke-channels-card">
          <h2 className="section-title">Reorder Channels</h2>
          {error && <p className="error-text">{error}</p>}
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={(event) => handleDragEnd(event, token)}
          >
            <SortableContext items={channels.map((channel) => channel.id)} strategy={verticalListSortingStrategy}>
              <ul className="channel-list">
                {channels.map((channel) => (
                  <SortableChannelItem key={channel.id} channel={channel} />
                ))}
              </ul>
            </SortableContext>
          </DndContext>
        </section>
      </div>

      {editChannel && (
        <div className="modal-overlay">
          <div className="modal-content">
            <h2 className="modal-title">Edit Channel</h2>
            <div className="edit-channel-form">
              <input
                type="text"
                value={editChannel.channelName}
                onChange={(e) => setEditChannel({ ...editChannel, channelName: e.target.value })}
                placeholder="Channel Name"
                className="karaoke-channels-input"
              />
              <input
                type="text"
                value={editChannel.channelId || ""}
                onChange={(e) => setEditChannel({ ...editChannel, channelId: e.target.value })}
                placeholder="Channel ID (optional)"
                className="karaoke-channels-input"
              />
              <label>
                <input
                  type="checkbox"
                  checked={editChannel.isActive}
                  onChange={(e) => setEditChannel({ ...editChannel, isActive: e.target.checked })}
                />
                Active
              </label>
              <div className="modal-buttons">
                <button
                  className="karaoke-channels-button save-button"
                  onClick={() => handleEditChannel(token)}
                >
                  Save
                </button>
                <button
                  className="karaoke-channels-button close-button"
                  onClick={() => setEditChannel(null)}
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
};

export default KaraokeChannelsPage;