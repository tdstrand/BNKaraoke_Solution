// src/pages/ChangePassword.tsx
import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import './ChangePassword.css';
import LogoDuet from '../assets/TwoSingerMnt.png';
import { API_ROUTES } from '../config/apiConfig';

const ChangePassword: React.FC = () => {
  const [currentPassword, setCurrentPassword] = useState<string>("");
  const [newPassword, setNewPassword] = useState<string>("");
  const [confirmNewPassword, setConfirmNewPassword] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [isForcedChange, setIsForcedChange] = useState<boolean>(false);
  const navigate = useNavigate();
  const currentPasswordRef = useRef<HTMLInputElement>(null);
  const newPasswordRef = useRef<HTMLInputElement>(null);
  const confirmNewPasswordRef = useRef<HTMLInputElement>(null);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[CHANGE_PASSWORD] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[CHANGE_PASSWORD] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[CHANGE_PASSWORD] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[CHANGE_PASSWORD] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[CHANGE_PASSWORD] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  useEffect(() => {
    validateToken();
    const mustChangePassword = localStorage.getItem("mustChangePassword") === "true";
    setIsForcedChange(mustChangePassword);
  }, []);

  const handleChangePassword = async () => {
    const token = validateToken();
    if (!token) return;

    if (!isForcedChange && !currentPassword) {
      setError("Please enter your current password");
      return;
    }
    if (!newPassword || !confirmNewPassword) {
      setError("Please enter and confirm your new password");
      return;
    }
    if (newPassword !== confirmNewPassword) {
      setError("New passwords do not match");
      return;
    }

    try {
      console.log(`[CHANGE_PASSWORD] Attempting change password fetch to: ${API_ROUTES.CHANGE_PASSWORD}`);
      const response = await fetch(API_ROUTES.CHANGE_PASSWORD, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          currentPassword: isForcedChange ? null : currentPassword,
          newPassword: newPassword
        }),
      });
      const responseText = await response.text();
      console.log("[CHANGE_PASSWORD] Change Password Raw Response:", responseText);
      if (!response.ok) {
        let errorData;
        try {
          errorData = JSON.parse(responseText);
        } catch {
          errorData = { message: "Password change failed" };
        }
        throw new Error(errorData.message || `Password change failed: ${response.status}`);
      }
      localStorage.setItem("mustChangePassword", "false");
      alert("Password changed successfully!");
      navigate("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("[CHANGE_PASSWORD] Change Password Error:", err);
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      if (!isForcedChange && !currentPassword && currentPasswordRef.current) {
        currentPasswordRef.current.focus();
      } else if (!newPassword && newPasswordRef.current) {
        newPasswordRef.current.focus();
      } else if (!confirmNewPassword && confirmNewPasswordRef.current) {
        confirmNewPasswordRef.current.focus();
      } else {
        handleChangePassword();
      }
    }
  };

  try {
    return (
      <div className="change-password-container mobile-change-password">
        <img src={LogoDuet} alt="BNKaraoke.com Logo" className="change-password-logo" />
        <div className="change-password-card">
          <h2 className="change-password-title">Change Password</h2>
          {error && <p className="change-password-error">{error}</p>}
          <div className="change-password-form">
            {!isForcedChange && (
              <>
                <label htmlFor="currentPassword">Current Password</label>
                <input
                  type="password"
                  id="currentPassword"
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="Enter current password"
                  aria-label="Current password"
                  className="change-password-input"
                  ref={currentPasswordRef}
                />
              </>
            )}
            <label htmlFor="newPassword">New Password</label>
            <input
              type="password"
              id="newPassword"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Enter new password"
              aria-label="New password"
              className="change-password-input"
              ref={newPasswordRef}
            />
            <label htmlFor="confirmNewPassword">Confirm New Password</label>
            <input
              type="password"
              id="confirmNewPassword"
              value={confirmNewPassword}
              onChange={(e) => setConfirmNewPassword(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Confirm new password"
              aria-label="Confirm new password"
              className="change-password-input"
              ref={confirmNewPasswordRef}
            />
            <button 
              onClick={handleChangePassword} 
              onTouchStart={handleChangePassword}
              className="change-password-button"
            >
              Change Password
            </button>
            <button 
              onClick={() => navigate("/dashboard")} 
              onTouchStart={() => navigate("/dashboard")}
              className="change-password-button secondary-button"
            >
              Back to Dashboard
            </button>
          </div>
          <p className="backlink">
            BPM data provided by <a href="https://getsongbpm.com" target="_blank" rel="noopener noreferrer">GetSongBPM</a>
          </p>
        </div>
      </div>
    );
  } catch (error) {
    console.error("[CHANGE_PASSWORD] Render error:", error);
    return <div>Error in ChangePassword: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default ChangePassword;