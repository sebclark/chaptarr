import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import { messageTypes } from 'Helpers/Props';
import { showMessage } from 'Store/Actions/appActions';
import { deleteBookFile, deleteBookFiles } from 'Store/Actions/bookFileActions';
import { executeCommand } from 'Store/Actions/commandActions';
import {
  fetchUnmappedFiles,
  gotoUnmappedFilesFirstPage,
  gotoUnmappedFilesLastPage,
  gotoUnmappedFilesNextPage,
  gotoUnmappedFilesPage,
  gotoUnmappedFilesPreviousPage,
  setUnmappedFilesTableOption
} from 'Store/Actions/unmappedFileActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import UnmappedFilesTable from './UnmappedFilesTable';

function createMapStateToProps() {
  return createSelector(
    (state) => state.unmappedFiles,
    createCommandExecutingSelector(commandNames.REFRESH_UNMAPPED_FILES),
    createCommandExecutingSelector(commandNames.RETRY_UNMAPPED_MATCH),
    createCommandExecutingSelector(commandNames.SEND_MATCHING_LOGS),
    createDimensionsSelector(),
    (
      unmappedFiles,
      isRefreshingFiles,
      isRetryMatching,
      isSendingLogs,
      dimensionsState
    ) => {
      // The server now returns one page of folders rather than every unmapped file,
      // so there is nothing to re-filter client side: the endpoint only ever selects
      // rows with no edition. totalRecords counts FOLDERS, not files, because paging
      // is folder-aligned to keep import units whole.
      const {
        items,
        ...otherProps
      } = unmappedFiles;

      return {
        items,
        ...otherProps,
        isRefreshingFiles,
        isRetryMatching,
        isSendingLogs,
        isSmallScreen: dimensionsState.isSmallScreen
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onTableOptionChange(payload) {
      dispatch(setUnmappedFilesTableOption(payload));
    },

    onFirstPagePress() {
      dispatch(gotoUnmappedFilesFirstPage());
    },

    onPreviousPagePress() {
      dispatch(gotoUnmappedFilesPreviousPage());
    },

    onNextPagePress() {
      dispatch(gotoUnmappedFilesNextPage());
    },

    onLastPagePress() {
      dispatch(gotoUnmappedFilesLastPage());
    },

    onPageSelect(page) {
      dispatch(gotoUnmappedFilesPage({ page }));
    },

    fetchUnmappedFiles() {
      dispatch(fetchUnmappedFiles());
    },

    deleteUnmappedFile(id) {
      dispatch(deleteBookFile({ id }));
    },

    deleteUnmappedFiles(bookFileIds) {
      dispatch(deleteBookFiles({ bookFileIds }));
    },

    previewMatchingLogsForReview(data) {
      return createAjaxRequest({
        url: '/matchinglog/preview',
        method: 'POST',
        data: JSON.stringify({
          mediaType: data.mediaType,
          unmappedFiles: data.unmappedFiles,
          maxEntries: 1000,
          failedMatchesOnly: true,
          daysBack: data.minutes ? 0 : 7,
          minutesBack: data.minutes || null
        }),
        dataType: 'json'
      }).request;
    },

    onRefreshUnmappedFilesPress(mediaType, unmappedFiles) {
      dispatch(executeCommand({
        name: commandNames.REFRESH_UNMAPPED_FILES,
        mediaType,
        unmappedFiles,
        commandFinished: () => {
          dispatch(fetchUnmappedFiles());
        }
      }));
    },

    onRetryUnmappedMatchPress(mediaType, unmappedFiles) {
      dispatch(executeCommand({
        name: commandNames.RETRY_UNMAPPED_MATCH,
        mediaType,
        unmappedFiles,
        commandFinished: () => {
          dispatch(fetchUnmappedFiles());
        }
      }));
    },

    sendMatchingLogsForReview(data) {
      dispatch(executeCommand({
        name: commandNames.SEND_MATCHING_LOGS,
        specificFilePaths: data.paths || [],
        mediaType: data.mediaType,
        unmappedFiles: data.unmappedFiles,
        maxEntries: 1000,
        failedMatchesOnly: true,
        daysBack: data.minutes ? 0 : 7,
        minutesBack: data.minutes || null,
        commandFinished: (command) => {
          const isFailure = command.status === 'failed';
          const progressMessage = command.body?.progressMessage;
          const message = isFailure ?
            (progressMessage || command.message || 'Failed to send matching logs for review') :
            (progressMessage || command.message || 'Matching logs sent for review');

          dispatch(showMessage({
            id: `send-matching-logs-${command.id}`,
            name: 'SendMatchingLogs',
            message,
            type: isFailure ? messageTypes.ERROR : messageTypes.SUCCESS,
            hideAfter: isFailure ? 10 : 6
          }));
        }
      }));
    }
  };
}

class UnmappedFilesTableConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    registerPagePopulator(this.repopulate, ['bookFileUpdated', 'bookFileSync']);

    this.repopulate();
  }

  componentWillUnmount() {
    unregisterPagePopulator(this.repopulate);
  }

  //
  // Control

  repopulate = () => {
    this.props.fetchUnmappedFiles();
  };

  //
  // Render

  render() {
    return (
      <UnmappedFilesTable
        {...this.props}
      />
    );
  }
}

UnmappedFilesTableConnector.propTypes = {
  isSmallScreen: PropTypes.bool.isRequired,
  isRefreshingFiles: PropTypes.bool.isRequired,
  isRetryMatching: PropTypes.bool.isRequired,
  isSendingLogs: PropTypes.bool.isRequired,
  onSortPress: PropTypes.func.isRequired,
  onTableOptionChange: PropTypes.func.isRequired,
  fetchUnmappedFiles: PropTypes.func.isRequired,
  deleteUnmappedFile: PropTypes.func.isRequired,
  deleteUnmappedFiles: PropTypes.func.isRequired,
  onRefreshUnmappedFilesPress: PropTypes.func.isRequired,
  onRetryUnmappedMatchPress: PropTypes.func.isRequired,
  previewMatchingLogsForReview: PropTypes.func.isRequired,
  sendMatchingLogsForReview: PropTypes.func.isRequired
};

export default withCurrentPage(
  connect(createMapStateToProps, createMapDispatchToProps)(UnmappedFilesTableConnector)
);
