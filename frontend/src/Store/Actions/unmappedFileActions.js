import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import serverSideCollectionHandlers from 'Utilities/serverSideCollectionHandlers';
import createHandleActions from './Creators/createHandleActions';
import createServerSideCollectionHandlers from './Creators/createServerSideCollectionHandlers';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'unmappedFiles';

//
// State

// Paging here is by FOLDER, not by file: import units are grouped per folder, so a
// page boundary inside one would show a fragment of a unit. totalRecords from the API
// is therefore a folder ("book group") count, which is what the page header reports.
// Ordering is by path on the server for the same reason - per-file sorting across a
// folder-aligned page is not meaningful, so no column is marked sortable.
export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  pageSize: 20,
  sortKey: 'path',
  sortDirection: sortDirections.ASCENDING,
  items: [],

  columns: [
    {
      name: 'select',
      columnLabel: 'Select',
      isSortable: false,
      isVisible: true,
      isModifiable: false,
      isHidden: true
    },
    {
      name: 'path',
      label: 'Path',
      isSortable: false,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'size',
      label: 'Size',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'dateAdded',
      label: 'Date Added',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'mediaType',
      label: 'Type',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'quality',
      label: 'Quality',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'actions',
      columnLabel: 'Actions',
      isVisible: true,
      isModifiable: false
    }
  ]
};

export const persistState = [
  'unmappedFiles.pageSize',
  'unmappedFiles.columns'
];

//
// Actions Types

export const FETCH_UNMAPPED_FILES = 'unmappedFiles/fetchUnmappedFiles';
export const GOTO_FIRST_UNMAPPED_FILES_PAGE = 'unmappedFiles/gotoUnmappedFilesFirstPage';
export const GOTO_PREVIOUS_UNMAPPED_FILES_PAGE = 'unmappedFiles/gotoUnmappedFilesPreviousPage';
export const GOTO_NEXT_UNMAPPED_FILES_PAGE = 'unmappedFiles/gotoUnmappedFilesNextPage';
export const GOTO_LAST_UNMAPPED_FILES_PAGE = 'unmappedFiles/gotoUnmappedFilesLastPage';
export const GOTO_UNMAPPED_FILES_PAGE = 'unmappedFiles/gotoUnmappedFilesPage';
export const SET_UNMAPPED_FILES_TABLE_OPTION = 'unmappedFiles/setUnmappedFilesTableOption';

//
// Action Creators

export const fetchUnmappedFiles = createThunk(FETCH_UNMAPPED_FILES);
export const gotoUnmappedFilesFirstPage = createThunk(GOTO_FIRST_UNMAPPED_FILES_PAGE);
export const gotoUnmappedFilesPreviousPage = createThunk(GOTO_PREVIOUS_UNMAPPED_FILES_PAGE);
export const gotoUnmappedFilesNextPage = createThunk(GOTO_NEXT_UNMAPPED_FILES_PAGE);
export const gotoUnmappedFilesLastPage = createThunk(GOTO_LAST_UNMAPPED_FILES_PAGE);
export const gotoUnmappedFilesPage = createThunk(GOTO_UNMAPPED_FILES_PAGE);
export const setUnmappedFilesTableOption = createAction(SET_UNMAPPED_FILES_TABLE_OPTION);

//
// Action Handlers

export const actionHandlers = handleThunks({
  ...createServerSideCollectionHandlers(
    section,
    '/bookfile/unmapped',
    fetchUnmappedFiles,
    {
      [serverSideCollectionHandlers.FETCH]: FETCH_UNMAPPED_FILES,
      [serverSideCollectionHandlers.FIRST_PAGE]: GOTO_FIRST_UNMAPPED_FILES_PAGE,
      [serverSideCollectionHandlers.PREVIOUS_PAGE]: GOTO_PREVIOUS_UNMAPPED_FILES_PAGE,
      [serverSideCollectionHandlers.NEXT_PAGE]: GOTO_NEXT_UNMAPPED_FILES_PAGE,
      [serverSideCollectionHandlers.LAST_PAGE]: GOTO_LAST_UNMAPPED_FILES_PAGE,
      [serverSideCollectionHandlers.EXACT_PAGE]: GOTO_UNMAPPED_FILES_PAGE
    }
  )
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_UNMAPPED_FILES_TABLE_OPTION]: createSetTableOptionReducer(section)

}, defaultState, section);
