import React from 'react';
import { useNavigate } from 'react-router-dom';
import './Profile.css';

const Profile: React.FC = () => {
  const navigate = useNavigate();

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
};

export default Profile;