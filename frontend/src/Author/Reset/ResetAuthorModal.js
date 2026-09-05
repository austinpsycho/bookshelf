import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import ResetAuthorModalContent from './ResetAuthorModalContent';

function ResetAuthorModal(props) {
  const {
    isOpen,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      size={sizes.MEDIUM}
      onModalClose={onModalClose}
    >
      <ResetAuthorModalContent
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

ResetAuthorModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ResetAuthorModal;
