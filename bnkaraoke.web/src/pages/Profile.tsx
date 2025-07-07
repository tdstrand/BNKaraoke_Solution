// src/pages/Profile.tsx
import React, { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import './Profile.css';

const Profile: React.FC = () => {
  const navigate = useNavigate();

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[PROFILE] No token or userName found");
      navigate("/login");
      return false;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[PROFILE] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return false;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[PROFILE] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return false;
      }
      console.log("[PROFILE] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return true;
    } catch (err) {
      console.error("[PROFILE] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      navigate("/login");
      return false;
    }
  };

  useEffect(() => {
    validateToken();
  }, []);

  try {
    return (
      <div className="profile-container">
        <h1 className="profile-title">User Profile</h1>
        <p className="profile-text">Profile page coming soon!</p>
        <button onClick={() => navigate("/change-password")} className="profile-button">
          Change Password
        </button>
        <button onClick={() => navigate("/dashboard")} className="profile-button secondary-button">
          Back to Dashboard
        </button>
      </div>
    );
  } catch (error) {
    console.error("[PROFILE] Render error:", error);
    return <div>Error in Profile: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default Profile;