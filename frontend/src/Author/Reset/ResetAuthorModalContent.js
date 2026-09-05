import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import styles from './ResetAuthorModalContent.css';

function normalize(value) {
  return (value || '')
    .toLowerCase()
    .replace(/[^a-z0-9 ]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

// Search ranks by relevance to the whole query, which routinely puts a
// different person above the one being reset -- searching "David Ludwig"
// returns "David Mitchell" first. Scoring the name itself puts a direct match
// on top regardless of where search placed it.
function nameScore(candidateName, wanted) {
  const a = normalize(candidateName);
  const b = normalize(wanted);

  if (!a || !b) {
    return 0;
  }
  if (a === b) {
    return 4;
  }
  if (a.startsWith(b) || b.startsWith(a)) {
    return 3;
  }
  if (a.includes(b) || b.includes(a)) {
    return 2;
  }
  return 0;
}

// The best match, or null when nothing resembles the name. A weak guess is
// worse than none: this rebuilds an author, and picking the wrong one is the
// failure the picker exists to prevent.
function bestGuess(candidates, wanted) {
  let best = null;
  let bestScore = 0;

  candidates.forEach((candidate, index) => {
    const score = nameScore(candidate.authorName, wanted);

    if (score > bestScore) {
      best = candidate;
      bestScore = score;
      return;
    }

    // Ties go to whichever search ranked higher.
    if (score === bestScore && score > 0 && best === null) {
      best = candidates[index];
    }
  });

  return bestScore > 0 ? best : null;
}

class ResetAuthorModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      term: props.authorName,
      isFetching: false,
      isResetting: false,
      candidates: [],
      selectedId: null,
      error: null
    };
  }

  componentDidMount() {
    this.search(this.props.authorName);
  }

  componentWillUnmount() {
    if (this.abort) {
      this.abort();
    }
  }

  //
  // Control

  search = (term) => {
    if (!term) {
      return;
    }

    if (this.abort) {
      this.abort();
    }

    this.setState({ isFetching: true, error: null });

    const { request, abortRequest } = createAjaxRequest({
      url: '/search',
      data: { term }
    });

    this.abort = abortRequest;

    request.done((data) => {
      const candidates = data
        .filter((result) => result.author)
        .map((result) => result.author);

      const guess = bestGuess(candidates, this.props.authorName);

      this.setState({
        isFetching: false,
        candidates,
        selectedId: guess ? guess.foreignAuthorId : null
      });
    });

    request.fail((xhr) => {
      if (xhr.statusText === 'abort') {
        return;
      }

      this.setState({
        isFetching: false,
        error: 'Search failed. Check that your metadata source is reachable.'
      });
    });
  };

  //
  // Listeners

  onTermChange = ({ value }) => {
    this.setState({ term: value });
  };

  onSearchPress = () => {
    this.search(this.state.term);
  };

  onCandidatePress = (foreignAuthorId) => {
    this.setState({ selectedId: foreignAuthorId });
  };

  onResetPress = () => {
    const { selectedId } = this.state;

    if (!selectedId) {
      return;
    }

    this.setState({ isResetting: true, error: null });

    createAjaxRequest({
      url: `/author/${this.props.authorId}/reset`,
      method: 'POST',
      contentType: 'application/json',
      data: JSON.stringify({ foreignAuthorId: selectedId })
    }).request.done(() => {
      this.setState({ isResetting: false });
      this.props.onModalClose();

      // The author's ID changes, so the current page no longer exists.
      window.location.href = `${window.Readarr.urlBase}/author`;
    }).fail((xhr) => {
      const message = xhr.responseJSON && xhr.responseJSON.length ?
        xhr.responseJSON[0].errorMessage :
        'Reset failed.';

      this.setState({ isResetting: false, error: message });
    });
  };

  //
  // Render

  render() {
    const { authorName, onModalClose } = this.props;
    const { term, isFetching, isResetting, candidates, selectedId, error } = this.state;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          Reset {authorName}
        </ModalHeader>

        <ModalBody>
          <Alert kind={kinds.WARNING}>
            This removes {authorName} and adds them back from the metadata
            source. Files on disk are kept and re-imported, but monitoring,
            history and any manual book fixes for this author are lost.
          </Alert>

          <div className={styles.searchRow}>
            <TextInput
              name="term"
              value={term}
              onChange={this.onTermChange}
            />

            <Button
              className={styles.searchButton}
              onPress={this.onSearchPress}
            >
              Search
            </Button>
          </div>

          {
            error ?
              <Alert kind={kinds.DANGER}>{error}</Alert> :
              null
          }

          {
            isFetching ? <LoadingIndicator /> : null
          }

          {
            !isFetching && !candidates.length ?
              <Alert kind={kinds.INFO}>No authors found for this search.</Alert> :
              null
          }

          {
            !isFetching && candidates.length ?
              <div className={styles.results}>
                {
                  candidates.map((candidate) => {
                    const isSelected = candidate.foreignAuthorId === selectedId;

                    return (
                      <div
                        key={candidate.foreignAuthorId}
                        className={isSelected ? styles.selected : styles.candidate}
                        role="button"
                        tabIndex={0}
                        onClick={() => this.onCandidatePress(candidate.foreignAuthorId)}
                        onKeyDown={() => this.onCandidatePress(candidate.foreignAuthorId)}
                      >
                        <div className={styles.candidateName}>{candidate.authorName}</div>
                        <div className={styles.candidateId}>{candidate.foreignAuthorId}</div>
                      </div>
                    );
                  })
                }
              </div> :
              null
          }
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            Cancel
          </Button>

          <SpinnerButton
            kind={kinds.DANGER}
            isDisabled={!selectedId}
            isSpinning={isResetting}
            onPress={this.onResetPress}
          >
            Reset Author
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

ResetAuthorModalContent.propTypes = {
  authorId: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ResetAuthorModalContent;
